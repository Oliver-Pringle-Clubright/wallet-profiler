import { describe, it, expect, vi } from "vitest";
import { quickcheck } from "../../src/offerings/quickcheck.js";
import type { ProfilerClient } from "../../src/profilerClient.js";

describe("quickcheck", () => {
  it("validates EVM + solana + ENS", () => {
    expect(quickcheck.validate({ address: "0x" + "a".repeat(40) }).valid).toBe(true);
    expect(quickcheck.validate({ address: "vitalik.eth" }).valid).toBe(true);
    expect(quickcheck.validate({ address: "9WzDXwBbmkg8ZTbNMqUxvQRAyrZzDsGYdLVL9zYtAWWM" }).valid).toBe(true);
  });
  it("rejects missing address", () => {
    expect(quickcheck.validate({}).valid).toBe(false);
  });
  it("executes GET /trust/{address}", async () => {
    const get = vi.fn(async () => ({ score: 90 }));
    const client: ProfilerClient = {
      profile: vi.fn(), profileBatch: vi.fn(), profileMultiChain: vi.fn(),
      compare: vi.fn(), get, storeDeliverable: vi.fn(),
    };
    await quickcheck.execute({ address: "0xabc" }, { client });
    expect(get).toHaveBeenCalledWith("/trust/0xabc", { chain: "ethereum" });
  });
});
