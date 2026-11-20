package com.applada.keycloak.ratelimit;

import java.net.InetAddress;
import java.nio.charset.StandardCharsets;
import java.security.GeneralSecurityException;
import java.util.HexFormat;
import java.util.List;
import java.util.Objects;
import java.util.UUID;

import javax.crypto.Mac;
import javax.crypto.spec.SecretKeySpec;

import redis.clients.jedis.JedisPooled;

public final class IpLoginRateLimiter {
    private static final String BEGIN_ATTEMPT_SCRIPT = """
            local now = redis.call('TIME')
            local nowMillis = tonumber(now[1]) * 1000 + math.floor(tonumber(now[2]) / 1000)
            local windowMillis = tonumber(ARGV[1])
            local threshold = tonumber(ARGV[2])
            local blockSeconds = tonumber(ARGV[3])
            local leaseMillis = tonumber(ARGV[4])

            local blockedTtl = redis.call('TTL', KEYS[3])
            if blockedTtl > 0 then
              return {2, blockedTtl}
            end

            redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', nowMillis - windowMillis)
            redis.call('ZREMRANGEBYSCORE', KEYS[2], '-inf', nowMillis)
            local failures = redis.call('ZCARD', KEYS[1])
            if failures >= threshold then
              redis.call('SET', KEYS[3], '1', 'NX', 'EX', blockSeconds)
              return {2, redis.call('TTL', KEYS[3])}
            end

            local inFlight = redis.call('ZCARD', KEYS[2])
            if failures + inFlight >= threshold then
              return {1, math.ceil(leaseMillis / 1000)}
            end

            redis.call('ZADD', KEYS[2], nowMillis + leaseMillis, ARGV[5])
            redis.call('PEXPIRE', KEYS[2], leaseMillis + 1000)
            return {0, 0}
            """;

    private static final String RECORD_FAILURE_SCRIPT = """
            local now = redis.call('TIME')
            local nowMillis = tonumber(now[1]) * 1000 + math.floor(tonumber(now[2]) / 1000)
            local windowMillis = tonumber(ARGV[1])
            local threshold = tonumber(ARGV[2])
            local blockSeconds = tonumber(ARGV[3])

            redis.call('ZREM', KEYS[2], ARGV[4])
            redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', nowMillis - windowMillis)
            redis.call('ZADD', KEYS[1], nowMillis, ARGV[4])
            redis.call('PEXPIRE', KEYS[1], windowMillis)

            local failures = redis.call('ZCARD', KEYS[1])
            if failures >= threshold then
              redis.call('SET', KEYS[3], '1', 'NX', 'EX', blockSeconds)
            end
            return failures
            """;

    private static final String COMPLETE_ATTEMPT_SCRIPT = """
            redis.call('ZREM', KEYS[1], ARGV[1])
            return 1
            """;

    private static final String RENEW_ATTEMPT_SCRIPT = """
            local now = redis.call('TIME')
            local nowMillis = tonumber(now[1]) * 1000 + math.floor(tonumber(now[2]) / 1000)
            local leaseMillis = tonumber(ARGV[2])
            if not redis.call('ZSCORE', KEYS[1], ARGV[1]) then
              return 0
            end
            redis.call('ZADD', KEYS[1], nowMillis + leaseMillis, ARGV[1])
            redis.call('PEXPIRE', KEYS[1], leaseMillis + 1000)
            return 1
            """;

    private final JedisPooled redis;
    private final byte[] hmacKey;
    private final Policy policy;

    public IpLoginRateLimiter(JedisPooled redis, byte[] hmacKey, Policy policy) {
        this.redis = Objects.requireNonNull(redis, "redis");
        this.hmacKey = Objects.requireNonNull(hmacKey, "hmacKey").clone();
        this.policy = Objects.requireNonNull(policy, "policy");
        if (this.hmacKey.length < 32) {
            throw new IllegalArgumentException("The rate-limit HMAC key must contain at least 32 bytes.");
        }
    }

    public AttemptDecision beginAttempt(String realmId, String ipAddress) {
        String attemptId = UUID.randomUUID().toString();
        Keys keys = keys(realmId, ipAddress);
        List<?> result = (List<?>) evaluate(
                BEGIN_ATTEMPT_SCRIPT,
                List.of(keys.failures(), keys.inFlight(), keys.blocked()),
                List.of(
                        Long.toString(policy.failureWindow().toMillis()),
                        Integer.toString(policy.maxFailedAttempts()),
                        Long.toString(policy.blockDuration().toSeconds()),
                        Long.toString(policy.reservationDuration().toMillis()),
                        attemptId));

        int status = ((Number) result.get(0)).intValue();
        long retryAfterSeconds = ((Number) result.get(1)).longValue();
        if (status == 0) {
            return new AttemptDecision(
                    Decision.ALLOWED,
                    new Attempt(keys.failures(), keys.inFlight(), keys.blocked(), attemptId),
                    0);
        }
        if (status == 2) {
            return new AttemptDecision(Decision.BLOCKED, null, Math.max(1, retryAfterSeconds));
        }
        return new AttemptDecision(Decision.BUSY, null, Math.max(1, retryAfterSeconds));
    }

    public int recordFailure(Attempt attempt) {
        Objects.requireNonNull(attempt, "attempt");
        Number failures = (Number) evaluate(
                RECORD_FAILURE_SCRIPT,
                List.of(attempt.failuresKey(), attempt.inFlightKey(), attempt.blockedKey()),
                List.of(
                        Long.toString(policy.failureWindow().toMillis()),
                        Integer.toString(policy.maxFailedAttempts()),
                        Long.toString(policy.blockDuration().toSeconds()),
                        attempt.id()));
        return failures.intValue();
    }

    public void complete(Attempt attempt) {
        Objects.requireNonNull(attempt, "attempt");
        evaluate(COMPLETE_ATTEMPT_SCRIPT, List.of(attempt.inFlightKey()), List.of(attempt.id()));
    }

    public void renew(Attempt attempt) {
        Objects.requireNonNull(attempt, "attempt");
        evaluate(
                RENEW_ATTEMPT_SCRIPT,
                List.of(attempt.inFlightKey()),
                List.of(attempt.id(), Long.toString(policy.reservationDuration().toMillis())));
    }

    public java.time.Duration reservationRenewalInterval() {
        return java.time.Duration.ofMillis(Math.max(1, policy.reservationDuration().toMillis() / 3));
    }

    private Object evaluate(String script, List<String> keys, List<String> arguments) {
        try {
            return redis.eval(script, keys, arguments);
        } catch (RuntimeException exception) {
            throw new RateLimitStoreException("The shared login rate-limit store is unavailable.", exception);
        }
    }

    private Keys keys(String realmId, String ipAddress) {
        if (realmId == null || realmId.isBlank()) {
            throw new IllegalArgumentException("A realm ID is required.");
        }
        String normalizedIp = normalizeIpAddress(ipAddress);
        String digest = hmacSha256(realmId + "\0" + normalizedIp);
        String prefix = "applada:login-rate:v1:{" + digest + "}:";
        return new Keys(prefix + "failures", prefix + "in-flight", prefix + "blocked");
    }

    private String normalizeIpAddress(String ipAddress) {
        if (ipAddress == null || !ipAddress.matches("[0-9A-Fa-f:.]+")) {
            throw new IllegalArgumentException("A numeric client IP address is required.");
        }
        try {
            return InetAddress.getByName(ipAddress).getHostAddress();
        } catch (java.net.UnknownHostException exception) {
            throw new IllegalArgumentException("A valid client IP address is required.", exception);
        }
    }

    private String hmacSha256(String value) {
        try {
            Mac mac = Mac.getInstance("HmacSHA256");
            mac.init(new SecretKeySpec(hmacKey, "HmacSHA256"));
            return HexFormat.of().formatHex(mac.doFinal(value.getBytes(StandardCharsets.UTF_8)));
        } catch (GeneralSecurityException exception) {
            throw new IllegalStateException("HMAC-SHA256 is unavailable.", exception);
        }
    }

    public record AttemptDecision(Decision decision, Attempt attempt, long retryAfterSeconds) {}

    public record Attempt(String failuresKey, String inFlightKey, String blockedKey, String id) {}

    public enum Decision {
        ALLOWED,
        BLOCKED,
        BUSY
    }

    public record Policy(
            int maxFailedAttempts,
            java.time.Duration failureWindow,
            java.time.Duration blockDuration,
            java.time.Duration reservationDuration) {

        public Policy {
            if (maxFailedAttempts < 1
                    || failureWindow.isZero()
                    || failureWindow.isNegative()
                    || blockDuration.isZero()
                    || blockDuration.isNegative()
                    || reservationDuration.isZero()
                    || reservationDuration.isNegative()) {
                throw new IllegalArgumentException("Rate-limit policy values must be positive.");
            }
        }

        public static Policy defaults() {
            return new Policy(
                    5,
                    java.time.Duration.ofMinutes(15),
                    java.time.Duration.ofMinutes(15),
                    java.time.Duration.ofSeconds(60));
        }
    }

    private record Keys(String failures, String inFlight, String blocked) {}

    public static final class RateLimitStoreException extends RuntimeException {
        public RateLimitStoreException(String message, Throwable cause) {
            super(message, cause);
        }
    }
}
