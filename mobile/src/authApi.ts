export interface UserProfile {
  id: string;
  email: string;
}

export interface AuthenticationSession {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
  user: UserProfile;
}

interface ProblemDetails {
  title?: string;
  detail?: string;
}

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

const apiBaseUrl = process.env.EXPO_PUBLIC_API_URL?.replace(/\/+$/, '');

export const isApiConfigured = Boolean(apiBaseUrl);

async function request<T>(
  path: string,
  options: {
    method?: 'GET' | 'POST';
    body?: unknown;
    accessToken?: string;
  } = {},
): Promise<T> {
  if (!apiBaseUrl) {
    throw new ApiError(0, 'Set EXPO_PUBLIC_API_URL in mobile/.env first.');
  }

  const headers: Record<string, string> = {
    Accept: 'application/json',
  };

  if (options.body !== undefined) {
    headers['Content-Type'] = 'application/json';
  }

  if (options.accessToken) {
    headers.Authorization = `Bearer ${options.accessToken}`;
  }

  const response = await fetch(`${apiBaseUrl}${path}`, {
    method: options.method ?? 'GET',
    headers,
    ...(options.body === undefined
      ? {}
      : { body: JSON.stringify(options.body) }),
  });

  if (!response.ok) {
    const body = await response.text();
    let problem: ProblemDetails | undefined;

    try {
      problem = JSON.parse(body) as ProblemDetails;
    } catch {
      problem = undefined;
    }

    throw new ApiError(
      response.status,
      problem?.detail ?? problem?.title ?? `Request failed (${response.status}).`,
    );
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

export const signInWithGoogle = (idToken: string) =>
  request<AuthenticationSession>('/api/v1/auth/google', {
    method: 'POST',
    body: { idToken },
  });

export const refreshSession = (refreshToken: string) =>
  request<AuthenticationSession>('/api/v1/auth/refresh', {
    method: 'POST',
    body: { refreshToken },
  });

export const getProfile = (accessToken: string) =>
  request<UserProfile>('/api/v1/auth/me', { accessToken });

export const revokeSession = (refreshToken: string) =>
  request<void>('/api/v1/auth/revoke', {
    method: 'POST',
    body: { refreshToken },
  });
