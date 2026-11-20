package com.applada.keycloak.ratelimit;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.nio.charset.StandardCharsets;
import java.time.Duration;

import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.testcontainers.containers.GenericContainer;
import org.testcontainers.junit.jupiter.Container;
import org.testcontainers.junit.jupiter.Testcontainers;
import org.testcontainers.utility.DockerImageName;

import redis.clients.jedis.JedisPooled;

@Testcontainers(disabledWithoutDocker = true)
class IpLoginRateLimiterIntegrationTest {
    @Container
    private static final GenericContainer<?> REDIS =
            new GenericContainer<>(DockerImageName.parse("redis:7.4-alpine"))
                    .withExposedPorts(6379);

    private JedisPooled redis;
    private IpLoginRateLimiter limiter;

    @BeforeEach
    void setUp() {
        redis = new JedisPooled(REDIS.getHost(), REDIS.getMappedPort(6379));
        redis.flushDB();
        limiter = new IpLoginRateLimiter(redis, "test-hmac-key-material-at-least-32-bytes".getBytes(StandardCharsets.UTF_8),
                IpLoginRateLimiter.Policy.defaults());
    }

    @AfterEach
    void tearDown() {
        redis.close();
    }

    @Test
    void blocksIpForFifteenMinutesAfterFiveFailedAttempts() {
        for (int attemptNumber = 0; attemptNumber < 5; attemptNumber++) {
            IpLoginRateLimiter.AttemptDecision decision = limiter.beginAttempt("realm-a", "203.0.113.7");

            assertEquals(IpLoginRateLimiter.Decision.ALLOWED, decision.decision());
            assertNotNull(decision.attempt());
            assertEquals(attemptNumber + 1, limiter.recordFailure(decision.attempt()));
        }

        IpLoginRateLimiter.AttemptDecision blocked = limiter.beginAttempt("realm-a", "203.0.113.7");

        assertEquals(IpLoginRateLimiter.Decision.BLOCKED, blocked.decision());
        assertTrue(blocked.retryAfterSeconds() > 0);
        assertTrue(blocked.retryAfterSeconds() <= Duration.ofMinutes(15).toSeconds());
    }

    @Test
    void reservesAtMostFiveConcurrentAttemptsPerIp() {
        for (int attemptNumber = 0; attemptNumber < 5; attemptNumber++) {
            assertEquals(
                    IpLoginRateLimiter.Decision.ALLOWED,
                    limiter.beginAttempt("realm-a", "203.0.113.8").decision());
        }

        assertEquals(
                IpLoginRateLimiter.Decision.BUSY,
                limiter.beginAttempt("realm-a", "203.0.113.8").decision());
    }

    @Test
    void renewsAnActiveReservationBeforeItsLeaseExpires() throws InterruptedException {
        IpLoginRateLimiter.Policy shortLease =
                new IpLoginRateLimiter.Policy(1, Duration.ofMinutes(15), Duration.ofMinutes(15), Duration.ofSeconds(3));
        IpLoginRateLimiter shortLeaseLimiter =
                new IpLoginRateLimiter(
                        redis,
                        "test-hmac-key-material-at-least-32-bytes".getBytes(StandardCharsets.UTF_8),
                        shortLease);
        IpLoginRateLimiter.Attempt activeAttempt =
                shortLeaseLimiter.beginAttempt("realm-a", "203.0.113.13").attempt();

        Thread.sleep(1_700);
        shortLeaseLimiter.renew(activeAttempt);
        Thread.sleep(1_600);

        assertEquals(
                IpLoginRateLimiter.Decision.BUSY,
                shortLeaseLimiter.beginAttempt("realm-a", "203.0.113.13").decision());
    }

    @Test
    void successfulAttemptsReleaseReservationsWithoutAddingFailures() {
        for (int attemptNumber = 0; attemptNumber < 8; attemptNumber++) {
            IpLoginRateLimiter.AttemptDecision decision = limiter.beginAttempt("realm-a", "203.0.113.9");

            assertEquals(IpLoginRateLimiter.Decision.ALLOWED, decision.decision());
            limiter.complete(decision.attempt());
        }
    }

    @Test
    void isolatesCountersByRealmAndIp() {
        for (int attemptNumber = 0; attemptNumber < 4; attemptNumber++) {
            IpLoginRateLimiter.AttemptDecision decision = limiter.beginAttempt("realm-a", "203.0.113.10");
            limiter.recordFailure(decision.attempt());
        }

        assertEquals(
                IpLoginRateLimiter.Decision.ALLOWED,
                limiter.beginAttempt("realm-b", "203.0.113.10").decision());
        assertEquals(
                IpLoginRateLimiter.Decision.ALLOWED,
                limiter.beginAttempt("realm-a", "203.0.113.11").decision());
    }

    @Test
    void expiresFailuresOutsideTheRollingWindow() throws InterruptedException {
        IpLoginRateLimiter.Policy shortWindow =
                new IpLoginRateLimiter.Policy(2, Duration.ofSeconds(1), Duration.ofSeconds(2), Duration.ofSeconds(1));
        IpLoginRateLimiter shortWindowLimiter =
                new IpLoginRateLimiter(
                        redis,
                        "test-hmac-key-material-at-least-32-bytes".getBytes(StandardCharsets.UTF_8),
                        shortWindow);
        IpLoginRateLimiter.AttemptDecision first = shortWindowLimiter.beginAttempt("realm-a", "203.0.113.12");
        shortWindowLimiter.recordFailure(first.attempt());

        Thread.sleep(1_100);

        assertEquals(
                IpLoginRateLimiter.Decision.ALLOWED,
                shortWindowLimiter.beginAttempt("realm-a", "203.0.113.12").decision());
    }
}
