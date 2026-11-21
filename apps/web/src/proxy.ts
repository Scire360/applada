export { auth as proxy } from "@/auth";

// Fora do proxy: rotas do Auth.js; o BFF /api/app (responde o próprio 401 em JSON);
// só a página pública /convite (não /convite/*); e arquivos estáticos.
export const config = {
  matcher: ["/((?!api/auth|api/app/|convite(?:\\.rsc|\\.segments(?:/.*)?)?$|_next/static|_next/image|favicon.ico|robots.txt|sitemap.xml).*)"],
};
