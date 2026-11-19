package com.applada.keycloak.ratelimit;

import static com.applada.keycloak.ratelimit.IpLoginRateLimiter.Decision.ALLOWED;

import java.util.concurrent.RejectedExecutionException;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.ScheduledFuture;
import java.util.concurrent.TimeUnit;

import jakarta.ws.rs.core.MultivaluedMap;
import jakarta.ws.rs.core.Response;
import org.jboss.logging.Logger;
import org.keycloak.authentication.AuthenticationFlowContext;
import org.keycloak.authentication.authenticators.browser.UsernamePasswordForm;
import org.keycloak.models.KeycloakSession;

final class RateLimitedUsernamePasswordForm extends UsernamePasswordForm {
    private static final Logger LOGGER = Logger.getLogger(RateLimitedUsernamePasswordForm.class);

    private final IpLoginRateLimiter rateLimiter;
    private final ScheduledExecutorService renewalScheduler;
    private IpLoginRateLimiter.Attempt activeAttempt;

    RateLimitedUsernamePasswordForm(
            KeycloakSession session,
            IpLoginRateLimiter rateLimiter,
            ScheduledExecutorService renewalScheduler) {
        super(session);
        this.rateLimiter = rateLimiter;
        this.renewalScheduler = renewalScheduler;
    }

    @Override
    public void action(AuthenticationFlowContext context) {
        MultivaluedMap<String, String> formData = context.getHttpRequest().getDecodedFormParameters();
        if (formData.containsKey("cancel")) {
            super.action(context);
            return;
        }

        if (!context.getHttpRequest().isProxyTrusted()) {
            LOGGER.error("The login request did not arrive through a trusted proxy.");
            respond(
                    context,
                    503,
                    30,
                    "appladaLoginRateLimitUnavailable");
            return;
        }

        IpLoginRateLimiter.AttemptDecision decision;
        try {
            decision = rateLimiter.beginAttempt(
                    context.getRealm().getId(),
                    context.getConnection().getRemoteAddr());
        } catch (IllegalArgumentException | IpLoginRateLimiter.RateLimitStoreException exception) {
            LOGGER.error("Unable to start a login attempt because rate-limit validation or storage failed.", exception);
            respond(
                    context,
                    503,
                    30,
                    "appladaLoginRateLimitUnavailable");
            return;
        }

        if (decision.decision() != ALLOWED) {
            respond(
                    context,
                    429,
                    decision.retryAfterSeconds(),
                    "appladaLoginRateLimited");
            return;
        }

        activeAttempt = decision.attempt();
        IpLoginRateLimiter.Attempt attempt = activeAttempt;
        long renewalIntervalMillis = rateLimiter.reservationRenewalInterval().toMillis();
        ScheduledFuture<?> renewal;
        try {
            renewal = renewalScheduler.scheduleWithFixedDelay(
                    () -> renewReservation(attempt),
                    renewalIntervalMillis,
                    renewalIntervalMillis,
                    TimeUnit.MILLISECONDS);
        } catch (RejectedExecutionException exception) {
            LOGGER.error("Unable to schedule renewal for a login attempt reservation.", exception);
            releaseReservation(context);
            respond(
                    context,
                    503,
                    30,
                    "appladaLoginRateLimitUnavailable");
            return;
        }
        try {
            super.action(context);
        } finally {
            renewal.cancel(false);
            releaseReservation(context);
        }
    }

    @Override
    public boolean validateUserAndPassword(
            AuthenticationFlowContext context,
            MultivaluedMap<String, String> inputData) {
        boolean valid = super.validateUserAndPassword(context, inputData);
        if (!valid) {
            recordFailure(context);
        }
        return valid;
    }

    private void recordFailure(AuthenticationFlowContext context) {
        if (activeAttempt == null) {
            return;
        }
        try {
            rateLimiter.recordFailure(activeAttempt);
            activeAttempt = null;
        } catch (IpLoginRateLimiter.RateLimitStoreException exception) {
            LOGGER.error("Unable to record a failed login attempt in the shared rate-limit store.", exception);
            respond(
                    context,
                    503,
                    30,
                    "appladaLoginRateLimitUnavailable");
        }
    }

    private void releaseReservation(AuthenticationFlowContext context) {
        if (activeAttempt == null) {
            return;
        }
        try {
            rateLimiter.complete(activeAttempt);
            activeAttempt = null;
        } catch (IpLoginRateLimiter.RateLimitStoreException exception) {
            LOGGER.error("Unable to release a login attempt reservation; it will expire automatically.", exception);
            respond(
                    context,
                    503,
                    30,
                    "appladaLoginRateLimitUnavailable");
        }
    }

    private void renewReservation(IpLoginRateLimiter.Attempt attempt) {
        if (attempt == null) {
            return;
        }
        try {
            rateLimiter.renew(attempt);
        } catch (IpLoginRateLimiter.RateLimitStoreException exception) {
            LOGGER.error("Unable to renew a login attempt reservation.", exception);
        }
    }

    private void respond(
            AuthenticationFlowContext context,
            int status,
            long retryAfterSeconds,
            String messageKey) {
        Response loginForm = context.form()
                .setError(messageKey)
                .createLoginUsernamePassword();
        Response response = Response.fromResponse(loginForm)
                .status(status)
                .header("Retry-After", Math.max(1, retryAfterSeconds))
                .build();
        context.forceChallenge(response);
    }
}
