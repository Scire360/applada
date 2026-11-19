import NextAuth from "next-auth";
import type { Profile } from "next-auth";
import Keycloak from "next-auth/providers/keycloak";
import { NextResponse } from "next/server";
import { hasAdminRole, isAdminPath } from "@/lib/authorization";

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

function getClientRoles(profile: Profile | undefined): string[] {
  const clientId = process.env.AUTH_KEYCLOAK_ID;
  const resourceAccess = profile?.resource_access;
  if (!clientId || !isRecord(resourceAccess)) {
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

export const { handlers, auth, signIn, signOut } = NextAuth(() => ({
  providers: isKeycloakConfigured()
    ? [Keycloak({ checks: ["pkce", "state", "nonce"] })]
    : [],
  pages: {
    signIn: "/login",
    error: "/login",
  },
  session: {
    strategy: "jwt",
    maxAge: 8 * 60 * 60,
  },
  callbacks: {
    jwt({ token, account, profile }) {
      if (account?.provider === "keycloak") {
        token.roles = getClientRoles(profile);
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
}));
