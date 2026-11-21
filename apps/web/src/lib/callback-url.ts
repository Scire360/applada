// Retorno depois do login: só caminho relativo da própria origem. Sem alias de import: roda em node --test.

const maxLength = 2048;

function decoded(value: string): string | null {
  try {
    return decodeURIComponent(value);
  } catch {
    return null;
  }
}

// Devolve o caminho se for seguro; senão "/". Recusa URL absoluta, "//host", barra invertida,
// caracteres de controle (também codificados) e o próprio /login.
export function safeCallbackPath(value: unknown): string {
  if (typeof value !== "string" || value.length === 0 || value.length > maxLength) return "/";
  if (!value.startsWith("/") || value.startsWith("//")) return "/";
  const plain = decoded(value);
  if (plain === null || /[\u0000-\u001f\u007f\\]/.test(plain) || plain.startsWith("//")) return "/";
  if (/^\/login(?:[/?#]|$)/.test(value)) return "/";
  return value;
}

// Link "Entrar novamente": volta para a tela atual depois do login.
export function loginHref(pathname: string, search: string): string {
  return `/login?callbackUrl=${encodeURIComponent(`${pathname}${search}`)}`;
}
