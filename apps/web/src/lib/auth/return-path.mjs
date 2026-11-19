/**
 * @param {unknown} value
 */
export function safeReturnPath(value) {
  if (
    typeof value !== "string"
    || !value.startsWith("/")
    || value.startsWith("//")
    || value.startsWith("/\\")
  ) {
    return "/";
  }

  try {
    const target = new URL(value, "https://applada.invalid");
    if (
      target.origin !== "https://applada.invalid"
      || target.pathname.startsWith("//")
      || target.pathname === "/login"
      || target.pathname.startsWith("/login/")
    ) {
      return "/";
    }

    return `${target.pathname}${target.search}${target.hash}`;
  } catch {
    return "/";
  }
}
