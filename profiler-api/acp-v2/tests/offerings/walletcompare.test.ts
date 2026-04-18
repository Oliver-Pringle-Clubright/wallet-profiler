// tests/offerings/walletcompare.test.ts
import { describe, it, expect, vi } from "vitest";
import { walletcompare } from "../../src/offerings/walletcompare.js";
import type { ProfilerClient } from "../../src/profilerClient.js";

describe("walletcompare", () => {
  it("rejects fewer than 2 addresses", () => {
    expect(walletcompare.validate({ addresses: ["0x" + "a".repeat(40)] }).valid).toBe(false);
  });
  it("rejects more than 10 addresses", () => {
    const addrs = Array.from({ length: 11 }, (_, i) => "0x" + String(i).padStart(40, "0"));
    expect(walletcompare.validate({ addresses: addrs }).valid).toBe(false);
  });
  it("rejects non-array", () => {
    expect(walletcompare.validate({ addresses: "0xabc,0xdef" }).valid).toBe(false);
  });
  it("executes compare()", async () => {
    const compare = vi.fn(async () => ({ result: true }));
    const client: ProfilerClient = {
      profile: vi.fn(), profileBatch: vi.fn(), profileMultiChain: vi.fn(),
      compare, get: vi.fn(), storeDeliverable: vi.fn(),
    };
    const a = "0x" + "a".repeat(40);
    const b = "0x" + "b".repeat(40);
    await walletcompare.execute({ addresses: [a, b] }, { client });
    expect(compare).toHaveBeenCalledWith({ addresses: [a, b], chain: "ethereum", tier: "standard" });
  });
});
