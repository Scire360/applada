import { createServerClient } from "@supabase/ssr";
import { NextResponse, type NextRequest } from "next/server";
import { safeReturnPath } from "@/lib/auth/return-path.mjs";
import { getSupabaseAuthConfig } from "@/lib/auth/config";
import { supabaseAuthCookieOptions } from "@/lib/auth/cookie-options";

function copyCookies(source: NextResponse, target: NextResponse) {
  for (const cookie of source.cookies.getAll()) {
    target.cookies.set(cookie);
  }
}

function redirectToLogin(request: NextRequest, response: NextResponse) {
  const loginUrl = request.nextUrl.clone();
  loginUrl.pathname = "/login";
  loginUrl.search = "";
  loginUrl.searchParams.set(
    "next",
    `${request.nextUrl.pathname}${request.nextUrl.search}`,
  );

  const redirectResponse = NextResponse.redirect(loginUrl);
  copyCookies(response, redirectResponse);
  return redirectResponse;
}

export async function proxy(request: NextRequest) {
  const config = getSupabaseAuthConfig();
  const isLoginPage = request.nextUrl.pathname === "/login";

  if (!config) {
    if (isLoginPage) {
      return NextResponse.next();
    }

    const loginUrl = request.nextUrl.clone();
    loginUrl.pathname = "/login";
    loginUrl.search = "";
    loginUrl.searchParams.set("status", "configuration");
    loginUrl.searchParams.set(
      "next",
      `${request.nextUrl.pathname}${request.nextUrl.search}`,
    );
    return NextResponse.redirect(loginUrl);
  }

  let response = NextResponse.next({ request });
  const supabase = createServerClient(config.url, config.anonKey, {
    cookieOptions: supabaseAuthCookieOptions,
    cookies: {
      getAll() {
        return request.cookies.getAll();
      },
      setAll(cookiesToSet) {
        for (const { name, value } of cookiesToSet) {
          request.cookies.set(name, value);
        }

        response = NextResponse.next({ request });
        for (const { name, value, options } of cookiesToSet) {
          response.cookies.set(name, value, options);
        }
      },
    },
  });

  const { data: { user }, error } = await supabase.auth.getUser();
  if (error && error.name !== "AuthSessionMissingError" && error.status !== 401) {
    console.error("Supabase session verification failed.", {
      code: error.code ?? "unknown",
    });
    return new NextResponse("Authentication service unavailable.", {
      status: 503,
      headers: { "Cache-Control": "no-store" },
    });
  }

  if (!user) {
    return isLoginPage
      ? response
      : redirectToLogin(request, response);
  }

  if (isLoginPage) {
    const destination = safeReturnPath(request.nextUrl.searchParams.get("next"));
    const redirectResponse = NextResponse.redirect(new URL(destination, request.url));
    copyCookies(response, redirectResponse);
    return redirectResponse;
  }

  request.headers.set("x-applada-auth-email", user.email ?? "");
  const authenticatedResponse = NextResponse.next({
    request: { headers: request.headers },
  });
  copyCookies(response, authenticatedResponse);
  return authenticatedResponse;
}

export const config = {
  matcher: ["/((?!_next/static|_next/image|favicon.ico|robots.txt|sitemap.xml).*)"],
};
