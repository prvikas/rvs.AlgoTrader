/** Decode a JWT and return its payload, or null if malformed. */
function decodePayload(token: string): Record<string, unknown> | null {
  try {
    return JSON.parse(atob(token.split('.')[1]))
  } catch {
    return null
  }
}

/** Returns true if the token exists, is a valid JWT, and has not expired. */
export function isTokenValid(token: string | null): boolean {
  if (!token) return false
  const payload = decodePayload(token)
  if (!payload || typeof payload.exp !== 'number') return false
  // exp is in seconds; add a 30s buffer so we don't use a token that's about to expire
  return payload.exp * 1000 > Date.now() + 30_000
}
