import { describe, it, expect } from "vitest";
import {
  validateAddress,
  validateEvmAddress,
  validateChain,
  validateTier,
  EVM_CHAINS,
  TIERS,
} from "../src/validators.js";

describe("validateAddress", () => {
  it("accepts hex address", () => {
    expect(validateAddress("0x" + "a".repeat(40))).toEqual({ valid: true });
  });
  it("accepts ENS", () => {
    expect(validateAddress("vitalik.eth")).toEqual({ valid: true });
  });
  it("accepts solana address", () => {
    expect(validateAddress("9WzDXwBbmkg8ZTbNMqUxvQRAyrZzDsGYdLVL9zYtAWWM")).toEqual({ valid: true });
  });
  it("rejects empty", () => {
    expect(validateAddress("")).toEqual({ valid: false, reason: "address is required" });
  });
  it("rejects garbage", () => {
    const res = validateAddress("not-an-address");
    expect(res.valid).toBe(false);
  });
});

describe("validateEvmAddress", () => {
  it("accepts hex", () => {
    expect(validateEvmAddress("0x" + "a".repeat(40))).toEqual({ valid: true });
  });
  it("rejects ENS", () => {
    expect(validateEvmAddress("vitalik.eth").valid).toBe(false);
  });
  it("rejects wrong length", () => {
    expect(validateEvmAddress("0xabc").valid).toBe(false);
  });
});

describe("validateChain", () => {
  it("accepts undefined (default)", () => {
    expect(validateChain(undefined)).toEqual({ valid: true });
  });
  it("accepts known chain", () => {
    expect(validateChain("ethereum")).toEqual({ valid: true });
  });
  it("rejects unknown chain", () => {
    const res = validateChain("fakechain");
    expect(res.valid).toBe(false);
    expect(res.reason).toMatch(/chain must be one of/);
  });
});

describe("validateTier", () => {
  it("accepts basic/standard/premium/free + undefined", () => {
    expect(validateTier(undefined).valid).toBe(true);
    expect(validateTier("free").valid).toBe(true);
    expect(validateTier("basic").valid).toBe(true);
    expect(validateTier("standard").valid).toBe(true);
    expect(validateTier("premium").valid).toBe(true);
  });
  it("rejects unknown", () => {
    expect(validateTier("platinum").valid).toBe(false);
  });
});

describe("constants", () => {
  it("EVM_CHAINS has 7 chains", () => {
    expect(EVM_CHAINS).toHaveLength(7);
  });
  it("TIERS has 4 tiers", () => {
    expect(TIERS).toEqual(["free", "basic", "standard", "premium"]);
  });
});
