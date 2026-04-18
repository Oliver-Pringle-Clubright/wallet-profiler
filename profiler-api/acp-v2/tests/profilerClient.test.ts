import { describe, it, expect, vi, beforeEach } from "vitest";
import { createProfilerClient } from "../src/profilerClient.js";

const BASE = "http://profiler-api:5000";

function mockFetch(response: { ok: boolean; status?: number; body: unknown }) {
  const fn = vi.fn(async (_input: string | URL | Request, _init?: RequestInit) => ({
    ok: response.ok,
    status: response.status ?? (response.ok ? 200 : 500),
    text: async () => (typeof response.body === "string" ? response.body : JSON.stringify(response.body)),
    json: async () => response.body,
  }));
  globalThis.fetch = fn as unknown as typeof fetch;
  return fn;
}

describe("profilerClient", () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it("profile() POSTs to /profile", async () => {
    const fetchFn = mockFetch({ ok: true, body: { score: 80 } });
    const client = createProfilerClient(BASE);
    const res = await client.profile({ address: "0xabc", chain: "ethereum", tier: "standard" });
    expect(res).toEqual({ score: 80 });
    expect(fetchFn).toHaveBeenCalledWith(
      `${BASE}/profile`,
      expect.objectContaining({
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ address: "0xabc", chain: "ethereum", tier: "standard" }),
      })
    );
  });

  it("profileBatch() POSTs to /profile/batch", async () => {
    const fetchFn = mockFetch({ ok: true, body: [{ a: 1 }, { a: 2 }] });
    const client = createProfilerClient(BASE);
    const res = await client.profileBatch({ addresses: ["0x1", "0x2"], chain: "base", tier: "standard" });
    expect(res).toEqual([{ a: 1 }, { a: 2 }]);
    const call = fetchFn.mock.calls[0]!;
    expect(call[0]).toBe(`${BASE}/profile/batch`);
  });

  it("get() adds query params when provided", async () => {
    const fetchFn = mockFetch({ ok: true, body: { ok: true } });
    const client = createProfilerClient(BASE);
    await client.get("/trust/0xabc", { chain: "ethereum" });
    expect(fetchFn.mock.calls[0]![0]).toBe(`${BASE}/trust/0xabc?chain=ethereum`);
  });

  it("throws on non-2xx with status + body", async () => {
    mockFetch({ ok: false, status: 500, body: "server blew up" });
    const client = createProfilerClient(BASE);
    await expect(client.get("/gas/0xabc")).rejects.toThrow(/500.*server blew up/);
  });

  it("storeDeliverable() POSTs JSON and returns id + url", async () => {
    const fetchFn = mockFetch({ ok: true, body: { id: "uuid-1", url: "http://host/deliverables/uuid-1" } });
    const client = createProfilerClient(BASE);
    const res = await client.storeDeliverable("job-123", { big: "payload" });
    expect(res).toEqual({ id: "uuid-1", url: "http://host/deliverables/uuid-1" });
    expect(fetchFn.mock.calls[0]![0]).toBe(`${BASE}/deliverables`);
  });
});
