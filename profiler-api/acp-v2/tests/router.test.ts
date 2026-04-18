import { describe, it, expect, vi } from "vitest";
import { route } from "../src/router.js";
import type { ProfilerClient } from "../src/profilerClient.js";

function ctx() {
  const client: ProfilerClient = {
    profile: vi.fn(async () => ({ score: 80 })),
    profileBatch: vi.fn(),
    profileMultiChain: vi.fn(),
    compare: vi.fn(),
    get: vi.fn(),
    storeDeliverable: vi.fn(),
  };
  return { client };
}

describe("route", () => {
  it("validates + executes known offering", async () => {
    const { client } = ctx();
    const res = await route("walletprofiler", { address: "0x" + "a".repeat(40) }, { client });
    expect(res.ok).toBe(true);
    if (res.ok) {
      expect(res.result).toEqual({ score: 80 });
    }
  });

  it("returns validation failure for unknown offering", async () => {
    const { client } = ctx();
    const res = await route("not-a-real-offering", {}, { client });
    expect(res.ok).toBe(false);
    if (!res.ok) {
      expect(res.reason).toMatch(/unknown offering/i);
    }
  });

  it("returns validation failure for bad requirements", async () => {
    const { client } = ctx();
    const res = await route("walletprofiler", {}, { client });
    expect(res.ok).toBe(false);
    if (!res.ok) {
      expect(res.reason).toMatch(/address/);
    }
  });

  it("catches execute errors and returns ok=false", async () => {
    const client: ProfilerClient = {
      profile: vi.fn(async () => {
        throw new Error("upstream 500");
      }),
      profileBatch: vi.fn(),
      profileMultiChain: vi.fn(),
      compare: vi.fn(),
      get: vi.fn(),
      storeDeliverable: vi.fn(),
    };
    const res = await route("walletprofiler", { address: "0x" + "a".repeat(40) }, { client });
    expect(res.ok).toBe(false);
    if (!res.ok) {
      expect(res.reason).toMatch(/upstream 500/);
    }
  });
});
