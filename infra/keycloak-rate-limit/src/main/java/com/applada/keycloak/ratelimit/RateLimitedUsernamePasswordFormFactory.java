package com.applada.keycloak.ratelimit;

import java.net.URI;
import java.util.Base64;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;

import org.keycloak.Config;
import org.keycloak.authentication.Authenticator;
import org.keycloak.authentication.authenticators.browser.UsernamePasswordFormFactory;
import org.keycloak.models.KeycloakSession;

import redis.clients.jedis.JedisPooled;

public final class RateLimitedUsernamePasswordFormFactory extends UsernamePasswordFormFactory {
    public static final String PROVIDER_ID = "applada-ip-rate-limit";

    private JedisPooled redis;
    private IpLoginRateLimiter rateLimiter;
    private ScheduledExecutorService renewalScheduler;

    @Override
    public void init(Config.Scope config) {
        String redisUrl = requiredEnvironmentVariable("APPLADA_AUTH_RATE_LIMIT_REDIS_URL");
        byte[] hmacKey = decodeHmacKey(requiredEnvironmentVariable("APPLADA_AUTH_RATE_LIMIT_HMAC_KEY"));
        redis = new JedisPooled(URI.create(redisUrl));
        rateLimiter = new IpLoginRateLimiter(redis, hmacKey, IpLoginRateLimiter.Policy.defaults());
        renewalScheduler = Executors.newSingleThreadScheduledExecutor(runnable -> {
            Thread thread = new Thread(runnable, "applada-login-rate-limit-renewal");
            thread.setDaemon(true);
            return thread;
        });
    }

    @Override
    public Authenticator create(KeycloakSession session) {
        if (rateLimiter == null || renewalScheduler == null) {
            throw new IllegalStateException("The Applada login rate limiter was not initialized.");
        }
        return new RateLimitedUsernamePasswordForm(session, rateLimiter, renewalScheduler);
    }

    @Override
    public String getId() {
        return PROVIDER_ID;
    }

    @Override
    public String getDisplayType() {
        return "Applada Username Password Form with IP Rate Limit";
    }

    @Override
    public String getHelpText() {
        return "Validates credentials and limits failed password attempts by trusted client IP.";
    }

    @Override
    public void close() {
        if (renewalScheduler != null) {
            renewalScheduler.shutdownNow();
            renewalScheduler = null;
        }
        if (redis != null) {
            redis.close();
            redis = null;
            rateLimiter = null;
        }
    }

    private String requiredEnvironmentVariable(String name) {
        String value = System.getenv(name);
        if (value == null || value.isBlank()) {
            throw new IllegalStateException("Required environment variable is missing: " + name);
        }
        return value;
    }

    private byte[] decodeHmacKey(String encodedKey) {
        try {
            byte[] key = Base64.getDecoder().decode(encodedKey);
            if (key.length < 32) {
                throw new IllegalStateException("The login rate-limit HMAC key must decode to at least 32 bytes.");
            }
            return key;
        } catch (IllegalArgumentException exception) {
            throw new IllegalStateException("The login rate-limit HMAC key must be Base64 encoded.", exception);
        }
    }
}
