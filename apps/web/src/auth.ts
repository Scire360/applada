import NextAuth from "next-auth";
import Keycloak from "next-auth/providers/keycloak";
import { NextResponse } from "next/server";
import { hasAdminRole, isAdminPath } from "@/lib/authorization";

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

function getClientRoles(profile: unknown): string[] {
  const clientId = process.env.AUTH_KEYCLOAK_ID;
  if (!clientId || !isRecord(profile)) {
    return [];
  }
  const resourceAccess = profile.resource_access;
  if (!isRecord(resourceAccess)) {
    return [];
  }

  const clientAccess = resourceAccess[clientId];
  if (!isRecord(clientAccess) || !Array.isArray(clientAccess.roles)) {
    return [];
  }

  return clientAccess.roles.filter(
    (role: unknown): role is string => typeof role === "string",
  );
}

export function isKeycloakConfigured() {
  return [
    process.env.AUTH_KEYCLOAK_ID,
    process.env.AUTH_KEYCLOAK_SECRET,
    process.env.AUTH_KEYCLOAK_ISSUER,
  ].every((value) => typeof value === "string" && value.trim().length > 0);
}

export function isAuthConfigured() {
  return isKeycloakConfigured() && Boolean(process.env.AUTH_SECRET);
}

export function isVisualPreview() {
  return process.env.NODE_ENV === "development"
    && !isAuthConfigured()
    && process.env.APPLADA_LOCAL_INTEGRATION !== "1";
}

function getPublicAppUrl(fallback?: string) {
  const configuredUrl = process.env.AUTH_URL ?? process.env.NEXTAUTH_URL;
  const appUrl =
    configuredUrl ??
    (process.env.NODE_ENV === "development"
      ? "http://localhost:3000"
      : fallback);

  if (!appUrl) {
    throw new Error("AUTH_URL must be configured for Keycloak logout.");
  }

  const parsedUrl = new URL(appUrl);
  if (
    process.env.NODE_ENV === "production" &&
    parsedUrl.protocol !== "https:"
  ) {
    throw new Error("AUTH_URL must use HTTPS in production.");
  }

  return parsedUrl.origin;
}

async function endKeycloakSession(idToken: unknown) {
  const issuer = process.env.AUTH_KEYCLOAK_ISSUER;
  const clientId = process.env.AUTH_KEYCLOAK_ID;
  if (!issuer || !clientId) {
    throw new Error("Keycloak issuer and client ID must be configured for logout.");
  }
  if (typeof idToken !== "string") {
    throw new Error("The Auth.js session is missing the Keycloak ID token for logout.");
  }

  const issuerUrl = new URL(issuer);
  if (
    process.env.NODE_ENV === "production" &&
    issuerUrl.protocol !== "https:"
  ) {
    throw new Error("AUTH_KEYCLOAK_ISSUER must use HTTPS in production.");
  }

  const logoutUrl = new URL(
    `${issuerUrl.pathname.replace(/\/+$/, "")}/protocol/openid-connect/logout`,
    issuerUrl.origin,
  );
  const body = new URLSearchParams({
    client_id: clientId,
    id_token_hint: idToken,
    post_logout_redirect_uri: new URL("/login", getPublicAppUrl()).toString(),
  });
  const response = await fetch(logoutUrl, {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body,
    cache: "no-store",
    redirect: "manual",
    signal: AbortSignal.timeout(5_000),
  });

  if (response.status !== 302 && response.status !== 303 && !response.ok) {
    throw new Error(`Keycloak SSO logout failed with status ${response.status}.`);
  }
}

async function getKeycloakUserInfo(
  accessToken: unknown,
): Promise<Record<string, unknown> | null> {
  const issuer = process.env.AUTH_KEYCLOAK_ISSUER;
  if (typeof accessToken !== "string" || !issuer) {
    throw new Error("Keycloak userinfo validation is not configured correctly.");
  }

  const issuerUrl = new URL(issuer);
  if (
    process.env.NODE_ENV === "production" &&
    issuerUrl.protocol !== "https:"
  ) {
    throw new Error("AUTH_KEYCLOAK_ISSUER must use HTTPS in production.");
  }

  const userInfoUrl = new URL(
    `${issuerUrl.pathname.replace(/\/+$/, "")}/protocol/openid-connect/userinfo`,
    issuerUrl.origin,
  );
  const response = await fetch(userInfoUrl, {
    headers: { Authorization: `Bearer ${accessToken}` },
    cache: "no-store",
    signal: AbortSignal.timeout(5_000),
  });

  if (response.status === 401) {
    return null;
  }
  if (!response.ok) {
    throw new Error(
      `Keycloak userinfo validation failed with status ${response.status}.`,
    );
  }

  const userInfo: unknown = await response.json();
  if (!isRecord(userInfo) || typeof userInfo.sub !== "string") {
    throw new Error("Keycloak returned an invalid userinfo response.");
  }

  return userInfo;
}

function assertProductionAuthConfiguration() {
  if (process.env.NODE_ENV !== "production") {
    return;
  }

  if (!isKeycloakConfigured()) {
    throw new Error("Keycloak OIDC settings must be configured in production.");
  }

  if (new TextEncoder().encode(process.env.AUTH_SECRET ?? "").length < 32) {
    throw new Error("AUTH_SECRET must contain at least 32 bytes in production.");
  }

  if (new URL(process.env.AUTH_KEYCLOAK_ISSUER!).protocol !== "https:") {
    throw new Error("AUTH_KEYCLOAK_ISSUER must use HTTPS in production.");
  }

  if (!process.env.AUTH_URL && !process.env.NEXTAUTH_URL) {
    throw new Error("AUTH_URL must be configured in production.");
  }
}

export const { handlers, auth, signIn, signOut } = NextAuth(() => {
  assertProductionAuthConfiguration();

  return {
    providers: isKeycloakConfigured()
      ? [Keycloak({ checks: ["pkce", "state", "nonce"] })]
      : [],
    useSecureCookies: process.env.NODE_ENV === "production",
    pages: {
      signIn: "/login",
      error: "/login",
    },
    session: {
      strategy: "jwt",
      maxAge: 60 * 60,
    },
    callbacks: {
      async jwt({ token, account }) {
        if (account?.provider === "keycloak") {
          if (typeof account.id_token !== "string") {
            throw new Error("Keycloak did not return an ID token for the OIDC session.");
          }
          if (typeof account.access_token !== "string") {
            throw new Error("Keycloak did not return an access token.");
          }
          const userInfo = await getKeycloakUserInfo(account.access_token);
          if (!userInfo) {
            return null;
          }

          token.roles = getClientRoles(userInfo);
          token.keycloakIdToken = account.id_token;
          token.keycloakAccessToken = account.access_token;
        } else {
          if (typeof token.keycloakAccessToken !== "string") {
            return null;
          }
          const userInfo = await getKeycloakUserInfo(token.keycloakAccessToken);
          if (!userInfo) {
            return null;
          }
          token.roles = getClientRoles(userInfo);
        }

        return token;
      },
      session({ session, token }) {
        if (session.user) {
          session.user.roles = Array.isArray(token.roles)
            ? token.roles.filter((role): role is string => typeof role === "string")
            : [];
        }

        return session;
      },
      authorized({ auth: session, request }) {
        const { pathname } = request.nextUrl;

        if (isVisualPreview()) {
          return true;
        }

        if (pathname === "/acesso-negado") {
          return true;
        }

        if (pathname === "/login") {
          return session?.user
            ? NextResponse.redirect(new URL("/", request.url))
            : true;
        }

        if (
          isAdminPath(pathname) &&
          session?.user &&
          !hasAdminRole(session.user.roles)
        ) {
          return NextResponse.redirect(new URL("/acesso-negado", request.url));
        }

        return Boolean(session?.user);
      },
    },
    events: {
      async signOut(message) {
        if ("token" in message) {
          await endKeycloakSession(message.token?.keycloakIdToken);
        }
      },
    },
  };
});
