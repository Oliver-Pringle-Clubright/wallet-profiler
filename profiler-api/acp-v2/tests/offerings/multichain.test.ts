// tests/offerings/multichain.test.ts
import { describe, it, expect, vi } from "vitest";
import { multichain } from "../../src/offerings/multichain.js";
import type { ProfilerClient } from "../../src/profilerClient.js";

describe("multichain", () => {
  it("rejects missing address", () => {
    expect(multichain.validate({}).valid).toBe(false);
  });
  it("accepts default (no chains)", () => {
    expect(multichain.validate({ address: "0x" + "a".repeat(40) }).valid).toBe(true);
  });
  it("rejects invalid chain in chains array", () => {
    expect(multichain.validate({ address: "0x" + "a".repeat(40), chains: ["ethereum", "fake"] }).valid).toBe(false);
  });
  it("rejects more than 5 chains", () => {
    const six = ["ethereum", "base", "arbitrum", "polygon", "optimism", "avalanche"];
    expect(multichain.validate({ address: "0x" + "a".repeat(40), chains: six }).valid).toBe(false);
  });
  it("executes with default tier=standard and all chains", async () => {
    const profileMultiChain = vi.fn(async () => ({ chains: {} }));
    const client: ProfilerClient = {
      profile: vi.fn(), profileBatch: vi.fn(), profileMultiChain,
      compare: vi.fn(), get: vi.fn(), storeDeliverable: vi.fn(),
    };
    await multichain.execute({ address: "0xabc" }, { client });
    const call = (profileMultiChain.mock.calls[0] as any[])[0] as any;
    expect(call.address).toBe("0xabc");
    expect(call.tier).toBe("standard");
    expect(call.chains).toEqual(["ethereum", "base", "arbitrum", "polygon", "optimism", "avalanche", "bnb"]);
  });
});
