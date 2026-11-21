// Cliente do navegador para o BFF /api/app. O token da API fica no servidor; aqui só há o cookie da sessão.

// Conta que a aba carregou; o BFF recusa gravar perfil ou foto se a sessão agora é de outra conta.
export const accountHeader = "X-Applada-Account";

export class ApiError extends Error {
  constructor(readonly status: number, message: string) {
    super(message);
    this.name = "ApiError";
  }
}

export async function appApi<T>(path: string, options?: RequestInit): Promise<T> {
  const response = await fetch(`/api/app/${path}`, { ...options, cache: "no-store" });
  if (!response.ok) {
    if (response.status === 401) {
      await response.body?.cancel();
      throw new ApiError(401, "Sua sessão expirou. Entre novamente.");
    }
    const problem: { title?: string; errors?: Record<string, string[]> } = await response.json().catch(() => ({}));
    const detail = problem.errors && Object.values(problem.errors).flat().join(" ");
    const retryAfter = response.headers.get("Retry-After");
    const message = response.status === 412
      ? "A partida mudou. Atualize os dados e revise a edição."
      : response.status === 429 && retryAfter
        ? `Aguarde ${retryAfter} s antes de tentar novamente.`
        : detail || problem.title || "A operação falhou. Tente novamente.";
    throw new ApiError(response.status, message);
  }
  return response.status === 204 ? undefined as T : response.json();
}
