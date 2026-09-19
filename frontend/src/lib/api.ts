import type { DownloadFormat, JobStatusDto, ResolveResponse } from "./types";

const API_URL = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:8080";

export class ApiError extends Error {
  constructor(
    message: string,
    public status: number
  ) {
    super(message);
  }
}

function getToken(): string | null {
  if (typeof window === "undefined") return null;
  return localStorage.getItem("ytpd_token");
}

async function request<T>(path: string, options: RequestInit = {}): Promise<T> {
  const token = getToken();
  const headers = new Headers(options.headers);
  headers.set("Content-Type", "application/json");
  if (token) headers.set("Authorization", `Bearer ${token}`);

  const res = await fetch(`${API_URL}${path}`, { ...options, headers });

  if (!res.ok) {
    let message = res.statusText;
    try {
      const body = await res.json();
      message = body.message ?? message;
    } catch {
      // ignore non-JSON error bodies
    }
    throw new ApiError(message, res.status);
  }

  if (res.status === 204) return undefined as T;
  return res.json() as Promise<T>;
}

export const api = {
  baseUrl: API_URL,

  login: (username: string, password: string) =>
    request<{ token: string; username: string }>("/api/auth/login", {
      method: "POST",
      body: JSON.stringify({ username, password }),
    }),

  resolve: (url: string) =>
    request<ResolveResponse>("/api/resolve", {
      method: "POST",
      body: JSON.stringify({ url }),
    }),

  createJob: (payload: {
    sourceUrl: string;
    items: { videoId: string; title: string; author: string }[];
    format: DownloadFormat;
    quality: string;
    embedMetadata: boolean;
  }) =>
    request<JobStatusDto>("/api/downloads", {
      method: "POST",
      body: JSON.stringify(payload),
    }),

  listJobs: () => request<JobStatusDto[]>("/api/downloads"),

  getJob: (id: string) => request<JobStatusDto>(`/api/downloads/${id}`),

  deleteJob: (id: string) => request<void>(`/api/downloads/${id}`, { method: "DELETE" }),

  downloadItemFile: (jobId: string, itemId: string, suggestedName: string) =>
    downloadWithAuth(`/api/downloads/${jobId}/items/${itemId}/file`, suggestedName),

  downloadJobZip: (jobId: string, suggestedName: string) =>
    downloadWithAuth(`/api/downloads/${jobId}/zip`, suggestedName),
};

// Downloads authenticate via a Bearer header, so plain <a href> links won't
// work for the file endpoints. Fetch as a blob instead and hand the browser
// an object URL to save, which also keeps the JWT out of the URL/history.
async function downloadWithAuth(path: string, suggestedName: string) {
  const token = getToken();
  const res = await fetch(`${API_URL}${path}`, {
    headers: token ? { Authorization: `Bearer ${token}` } : undefined,
  });
  if (!res.ok) throw new ApiError(res.statusText, res.status);

  const blob = await res.blob();
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = suggestedName;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
}

export function getStoredToken() {
  return getToken();
}
