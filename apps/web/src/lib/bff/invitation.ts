// O convite guardado só é descartado quando a API respondeu de forma definitiva.
// Em 401, 403, 429 e 5xx ele fica, para um novo login ou nova tentativa não perder o convite.
export function shouldClearInvitationCookie(status: number) {
  return (status >= 200 && status < 300) || status === 400 || status === 404 || status === 409;
}
