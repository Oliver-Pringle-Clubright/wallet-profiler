// tests/deliverable.test.ts
import { describe, it, expect, vi } from "vitest";
import { toDeliverable, INLINE_SIZE_LIMIT_BYTES } from "../src/deliverable.js";
import type { ProfilerClient } from "../src/profilerClient.js";

function mockClient() {
  const storeDeliverable = vi.fn(async () => ({ id: "abc", url: "http://host/deliverables/abc" }));
  const client: ProfilerClient = {
    profile: vi.fn(), profileBatch: vi.fn(), profileMultiChain: vi.fn(),
    compare: vi.fn(), get: vi.fn(), storeDeliverable,
  };
  return { client, storeDeliverable };
}

describe("toDeliverable", () => {
  it("returns inline JSON under the threshold", async () => {
    const { client, storeDeliverable } = mockClient();
    const payload = { score: 80, tags: ["a", "b"] };
    const out = await toDeliverable("job-1", payload, client);
    expect(out).toBe(JSON.stringify(payload));
    expect(storeDeliverable).not.toHaveBeenCalled();
  });

  it("returns URL when over the threshold", async () => {
    const { client, storeDeliverable } = mockClient();
    const big = { blob: "x".repeat(INLINE_SIZE_LIMIT_BYTES + 100) };
    const out = await toDeliverable("job-2", big, client);
    expect(out).toBe("http://host/deliverables/abc");
    expect(storeDeliverable).toHaveBeenCalledWith("job-2", big);
  });

  it("falls back to inline if storeDeliverable throws", async () => {
    const storeDeliverable = vi.fn(async () => { throw new Error("redis down"); });
    const client: ProfilerClient = {
      profile: vi.fn(), profileBatch: vi.fn(), profileMultiChain: vi.fn(),
      compare: vi.fn(), get: vi.fn(), storeDeliverable,
    };
    const big = { blob: "x".repeat(INLINE_SIZE_LIMIT_BYTES + 100) };
    const out = await toDeliverable("job-3", big, client);
    expect(out).toBe(JSON.stringify(big));
  });
});
