import { describe, it, expect, vi } from "vitest";
import { walletstatus } from "../../src/offerings/walletstatus.js";
import type { ProfilerClient } from "../../src/profilerClient.js";

describe("walletstatus", () => {
  it("rejects missing address", () => {
    expect(walletstatus.validate({}).valid).toBe(false);
  });
  it("accepts solana address", () => {
    expect(walletstatus.validate({ address: "9WzDXwBbmkg8ZTbNMqUxvQRAyrZzDsGYdLVL9zYtAWWM" }).valid).toBe(true);
  });
  it("executes GET /status/{address}", async () => {
    const get = vi.fn(async () => ({ balance: "1.5" }));
    const client: ProfilerClient = {
      profile: vi.fn(), profileBatch: vi.fn(), profileMultiChain: vi.fn(),
      compare: vi.fn(), get, storeDeliverable: vi.fn(),
    };
    await walletstatus.execute({ address: "0xabc", chain: "base" }, { client });
    expect(get).toHaveBeenCalledWith("/status/0xabc", { chain: "base" });
  });
});
