export interface ProfileRequest {
  address: string;
  chain?: string;
  tier?: string;
}

export interface BatchRequest {
  addresses: string[];
  chain?: string;
  tier?: string;
}

export interface MultiChainRequest {
  address: string;
  chains?: string[];
  tier?: string;
}

export interface CompareRequest {
  addresses: string[];
  chain?: string;
  tier?: string;
}

export interface StoredDeliverable {
  id: string;
  url: string;
}

export interface ProfilerClient {
  profile(req: ProfileRequest): Promise<unknown>;
  profileBatch(req: BatchRequest): Promise<unknown>;
  profileMultiChain(req: MultiChainRequest): Promise<unknown>;
  compare(req: CompareRequest): Promise<unknown>;
  get(path: string, query?: Record<string, string | number | undefined>): Promise<unknown>;
  storeDeliverable(jobId: string, payload: unknown): Promise<StoredDeliverable>;
}

export function createProfilerClient(baseUrl: string, timeoutMs = 60_000): ProfilerClient {
  async function post<T>(path: string, body: unknown): Promise<T> {
    const ctl = new AbortController();
    const timer = setTimeout(() => ctl.abort(), timeoutMs);
    try {
      const res = await fetch(`${baseUrl}${path}`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
        signal: ctl.signal,
      });
      if (!res.ok) {
        const text = await res.text();
        throw new Error(`profiler-api ${res.status}: ${text}`);
      }
      return (await res.json()) as T;
    } finally {
      clearTimeout(timer);
    }
  }

  async function get<T>(
    path: string,
    query?: Record<string, string | number | undefined>
  ): Promise<T> {
    const qs = query
      ? "?" +
        Object.entries(query)
          .filter(([, v]) => v !== undefined && v !== "")
          .map(([k, v]) => `${encodeURIComponent(k)}=${encodeURIComponent(String(v))}`)
          .join("&")
      : "";
    const ctl = new AbortController();
    const timer = setTimeout(() => ctl.abort(), timeoutMs);
    try {
      const res = await fetch(`${baseUrl}${path}${qs}`, { signal: ctl.signal });
      if (!res.ok) {
        const text = await res.text();
        throw new Error(`profiler-api ${res.status}: ${text}`);
      }
      return (await res.json()) as T;
    } finally {
      clearTimeout(timer);
    }
  }

  return {
    profile: (req) => post("/profile", req),
    profileBatch: (req) => post("/profile/batch", req),
    profileMultiChain: (req) => post("/profile/multi-chain", req),
    compare: (req) => post("/compare", req),
    get: (path, query) => get(path, query),
    storeDeliverable: (jobId, payload) =>
      post<StoredDeliverable>("/deliverables", { jobId, payload }),
  };
}
