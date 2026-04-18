// tests/offerings/tokenholders.test.ts
import { describe, it, expect, vi } from "vitest";
import { tokenholders } from "../../src/offerings/tokenholders.js";
import type { ProfilerClient } from "../../src/profilerClient.js";

describe("tokenholders", () => {
  it("requires contract", () => {
    expect(tokenholders.validate({}).valid).toBe(false);
  });
  it("rejects non-hex contract", () => {
    expect(tokenholders.validate({ contract: "not-hex" }).valid).toBe(false);
  });
  it("executes GET /token/{contract}/holders", async () => {
    const get = vi.fn(async () => ({ holders: [] }));
    const client: ProfilerClient = {
      profile: vi.fn(), profileBatch: vi.fn(), profileMultiChain: vi.fn(),
      compare: vi.fn(), get, storeDeliverable: vi.fn(),
    };
    const contract = "0x" + "c".repeat(40);
    await tokenholders.execute({ contract, chain: "base" }, { client });
    expect(get).toHaveBeenCalledWith(`/token/${contract}/holders`, { chain: "base" });
  });
});
