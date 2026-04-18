import { describe, it, expect, vi } from "vitest";
import { walletprofiler } from "../../src/offerings/walletprofiler.js";
import type { ProfilerClient } from "../../src/profilerClient.js";

function mockClient(): { client: ProfilerClient; profile: any; profileBatch: any } {
  const profile = vi.fn(async () => ({ score: 80 }));
  const profileBatch = vi.fn(async () => [{ score: 80 }, { score: 70 }]);
  const client: ProfilerClient = {
    profile, profileBatch,
    profileMultiChain: vi.fn(), compare: vi.fn(), get: vi.fn(), storeDeliverable: vi.fn(),
  };
  return { client, profile, profileBatch };
}

describe("walletprofiler", () => {
  it("validates: rejects missing address", () => {
    expect(walletprofiler.validate({}).valid).toBe(false);
  });
  it("validates: rejects unknown chain", () => {
    expect(walletprofiler.validate({ address: "0x" + "a".repeat(40), chain: "fake" }).valid).toBe(false);
  });
  it("validates: rejects unknown tier", () => {
    expect(walletprofiler.validate({ address: "0x" + "a".repeat(40), tier: "platinum" }).valid).toBe(false);
  });
  it("validates: accepts comma-separated addresses", () => {
    const a = "0x" + "a".repeat(40);
    const b = "0x" + "b".repeat(40);
    expect(walletprofiler.validate({ address: `${a},${b}` }).valid).toBe(true);
  });

  it("executes single profile", async () => {
    const { client, profile } = mockClient();
    await walletprofiler.execute({ address: "0xabc", chain: "ethereum" }, { client });
    expect(profile).toHaveBeenCalledWith({ address: "0xabc", chain: "ethereum", tier: "standard" });
  });
  it("executes batch for comma-separated", async () => {
    const { client, profileBatch } = mockClient();
    await walletprofiler.execute({ address: "0xabc,0xdef" }, { client });
    expect(profileBatch).toHaveBeenCalledWith({
      addresses: ["0xabc", "0xdef"],
      chain: "ethereum",
      tier: "standard",
    });
  });
});
