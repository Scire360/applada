export const APPLADA_ADMIN_ROLE = "applada-admin";

export function hasAdminRole(roles: readonly string[]): boolean {
  return roles.includes(APPLADA_ADMIN_ROLE);
}

export function isAdminPath(pathname: string): boolean {
  return pathname === "/administracao" || pathname.startsWith("/administracao/");
}
