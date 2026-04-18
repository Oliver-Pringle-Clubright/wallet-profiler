import { describe, it, expect } from "vitest";
import { loadEnv } from "../src/env.js";

describe("loadEnv", () => {
  it("returns all required vars when present", () => {
    const env = loadEnv({
      ACP_WALLET_ADDRESS: "0xabc",
      ACP_WALLET_ID: "wid-1",
      ACP_SIGNER_PRIVATE_KEY: "0xkey",
      ACP_CHAIN: "baseSepolia",
      PROFILER_API_URL: "http://profiler-api:5000",
    });
    expect(env.walletAddress).toBe("0xabc");
    expect(env.walletId).toBe("wid-1");
    expect(env.signerPrivateKey).toBe("0xkey");
    expect(env.chain).toBe("baseSepolia");
    expect(env.profilerApiUrl).toBe("http://profiler-api:5000");
    expect(env.builderCode).toBeUndefined();
  });

  it("passes through optional builderCode", () => {
    const env = loadEnv({
      ACP_WALLET_ADDRESS: "0xabc",
      ACP_WALLET_ID: "wid-1",
      ACP_SIGNER_PRIVATE_KEY: "0xkey",
      ACP_CHAIN: "base",
      PROFILER_API_URL: "http://profiler-api:5000",
      ACP_BUILDER_CODE: "bc-123",
    });
    expect(env.builderCode).toBe("bc-123");
  });

  it("throws on missing required var with var name in message", () => {
    expect(() =>
      loadEnv({
        ACP_WALLET_ID: "wid-1",
        ACP_SIGNER_PRIVATE_KEY: "0xkey",
        ACP_CHAIN: "base",
        PROFILER_API_URL: "http://profiler-api:5000",
      })
    ).toThrow(/ACP_WALLET_ADDRESS/);
  });

  it("throws on invalid ACP_CHAIN", () => {
    expect(() =>
      loadEnv({
        ACP_WALLET_ADDRESS: "0xabc",
        ACP_WALLET_ID: "wid-1",
        ACP_SIGNER_PRIVATE_KEY: "0xkey",
        ACP_CHAIN: "ethereum",
        PROFILER_API_URL: "http://profiler-api:5000",
      })
    ).toThrow(/ACP_CHAIN/);
  });

  it("throws on whitespace-only required var", () => {
    expect(() =>
      loadEnv({
        ACP_WALLET_ADDRESS: "   ",
        ACP_WALLET_ID: "wid-1",
        ACP_SIGNER_PRIVATE_KEY: "0xkey",
        ACP_CHAIN: "base",
        PROFILER_API_URL: "http://profiler-api:5000",
      })
    ).toThrow(/ACP_WALLET_ADDRESS/);
  });

  it("maps whitespace-only ACP_BUILDER_CODE to undefined", () => {
    const env = loadEnv({
      ACP_WALLET_ADDRESS: "0xabc",
      ACP_WALLET_ID: "wid-1",
      ACP_SIGNER_PRIVATE_KEY: "0xkey",
      ACP_CHAIN: "base",
      PROFILER_API_URL: "http://profiler-api:5000",
      ACP_BUILDER_CODE: "  ",
    });
    expect(env.builderCode).toBeUndefined();
  });
});
