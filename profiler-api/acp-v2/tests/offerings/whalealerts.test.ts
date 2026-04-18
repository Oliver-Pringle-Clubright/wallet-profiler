// tests/offerings/whalealerts.test.ts
import { describe, it, expect, vi } from "vitest";
import { whalealerts } from "../../src/offerings/whalealerts.js";
import type { ProfilerClient } from "../../src/profilerClient.js";

describe("whalealerts", () => {
  it("accepts no arguments", () => {
    expect(whalealerts.validate({}).valid).toBe(true);
  });
  it("rejects hours > 72", () => {
    expect(whalealerts.validate({ hours: 100 }).valid).toBe(false);
  });
  it("rejects unknown chain", () => {
    expect(whalealerts.validate({ chain: "solana" }).valid).toBe(false);
  });
  it("executes with defaults", async () => {
    const get = vi.fn(async () => ({ whales: [] }));
    const client: ProfilerClient = {
      profile: vi.fn(), profileBatch: vi.fn(), profileMultiChain: vi.fn(),
      compare: vi.fn(), get, storeDeliverable: vi.fn(),
    };
    await whalealerts.execute({}, { client });
    expect(get).toHaveBeenCalledWith("/whales/ethereum/recent", { hours: 24, minValue: 100000 });
  });
});
