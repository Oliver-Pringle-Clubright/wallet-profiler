// tests/offerings/deepanalysis.test.ts
import { describe, it, expect, vi } from "vitest";
import { deepanalysis } from "../../src/offerings/deepanalysis.js";
import type { ProfilerClient } from "../../src/profilerClient.js";

function client(): { c: ProfilerClient; batch: any; compare: any; multi: any } {
  const batch = vi.fn(async () => [{}, {}]);
  const compare = vi.fn(async () => ({ overlap: 0.5 }));
  const multi = vi.fn(async () => ({ chains: {} }));
  return {
    c: { profile: vi.fn(), profileBatch: batch, profileMultiChain: multi, compare, get: vi.fn(), storeDeliverable: vi.fn() },
    batch, compare, multi,
  };
}

describe("deepanalysis", () => {
  it("accepts 'all' as chain", () => {
    expect(deepanalysis.validate({ address: "0x" + "a".repeat(40), chain: "all" }).valid).toBe(true);
  });
  it("rejects invalid chain", () => {
    expect(deepanalysis.validate({ address: "0x" + "a".repeat(40), chain: "fake" }).valid).toBe(false);
  });
  it("single address + chain=all → multichain premium", async () => {
    const { c, multi } = client();
    await deepanalysis.execute({ address: "0xabc", chain: "all" }, { client: c });
    expect(multi).toHaveBeenCalledWith({ address: "0xabc", chains: expect.any(Array), tier: "premium" });
  });
  it("single address + specific chain → profile premium", async () => {
    const { c } = client();
    const spy = c.profile as any;
    spy.mockResolvedValueOnce({ score: 99 });
    await deepanalysis.execute({ address: "0xabc", chain: "ethereum" }, { client: c });
    expect(spy).toHaveBeenCalledWith({ address: "0xabc", chain: "ethereum", tier: "premium" });
  });
  it("multi address (2-10) → batch + compare", async () => {
    const { c, batch, compare } = client();
    const a = "0x" + "a".repeat(40);
    const b = "0x" + "b".repeat(40);
    await deepanalysis.execute({ address: `${a},${b}` }, { client: c });
    expect(batch).toHaveBeenCalled();
    expect(compare).toHaveBeenCalled();
  });
});
