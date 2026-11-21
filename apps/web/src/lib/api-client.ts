// Cliente do navegador para o BFF /api/app. O token da API fica no servidor; aqui só há o cookie da sessão.

// Conta que a aba carregou; o BFF recusa gravar perfil ou foto se a sessão agora é de outra conta.
export const accountHeader = "X-Applada-Account";

export class ApiError extends Error {
  readonly status: number;
  readonly code?: string;

  constructor(status: number, message: string, code?: string) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.code = code;
  }
}

export async function appApi<T>(path: string, options?: RequestInit): Promise<T> {
  let response: Response | undefined;
  try {
    response = await fetch(`/api/app/${path}`, { ...options, cache: "no-store" });
    if (response.ok) {
      return response.status === 204 ? undefined as T : await response.json();
    }
  } catch (failure) {
    // Fetch também pode falhar durante a leitura do corpo, depois de receber os cabeçalhos.
    if (options?.signal?.aborted || (response && !(failure instanceof TypeError))) throw failure;
    throw new ApiError(0, "Não foi possível conectar ao Applada. Verifique sua conexão e tente novamente.");
  }
  if (response.status === 401) {
    await response.body?.cancel();
    throw new ApiError(401, "Sua sessão expirou. Entre novamente.");
  }
  const problem: { title?: string; type?: string; errors?: Record<string, string[]> } = (await response.json().catch(() => null)) ?? {};
  const detail = problem.errors && Object.values(problem.errors).flat().join(" ");
  const retryAfter = response.headers.get("Retry-After");
  const message = response.status === 412
    ? "A partida mudou. Atualize os dados e revise a edição."
    : response.status === 429 && retryAfter
      ? `Aguarde ${retryAfter} s antes de tentar novamente.`
      : detail || problem.title || "A operação falhou. Tente novamente.";
  const prefix = "urn:applada:problem:";
  const code = typeof problem.type === "string" && problem.type.startsWith(prefix) ? problem.type.slice(prefix.length) : undefined;
  throw new ApiError(response.status, message, code);
}
