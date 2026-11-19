export const supabaseAuthCookieOptions = {
  name: process.env.NODE_ENV === "production"
    ? "__Host-applada-auth"
    : "applada-auth",
  httpOnly: true,
  secure: process.env.NODE_ENV === "production",
  sameSite: "lax" as const,
  path: "/",
};
