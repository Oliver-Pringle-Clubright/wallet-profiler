import { describe, it, expect } from "vitest";
import { priceFor, tierUsdc, OFFERING_OVERRIDES, TIER_USDC } from "../src/pricing.js";

describe("pricing tables", () => {
  it("TIER_USDC has 4 entries", () => {
    expect(TIER_USDC.free).toBe(0);
    expect(TIER_USDC.basic).toBe(1);
    expect(TIER_USDC.standard).toBe(2);
    expect(TIER_USDC.premium).toBe(5);
  });
  it("whalealerts override is $10", () => {
    expect(OFFERING_OVERRIDES.whalealerts).toBe(10);
  });
  it("quickcheck override is $0.50", () => {
    expect(OFFERING_OVERRIDES.quickcheck).toBe(0.5);
  });
});

describe("tierUsdc", () => {
  it("defaults to standard when tier is undefined", () => {
    expect(tierUsdc(undefined)).toBe(2);
  });
  it("maps free=0, basic=1, standard=2, premium=5", () => {
    expect(tierUsdc("free")).toBe(0);
    expect(tierUsdc("basic")).toBe(1);
    expect(tierUsdc("standard")).toBe(2);
    expect(tierUsdc("premium")).toBe(5);
  });
});

describe("priceFor", () => {
  it("override offering ignores tier", () => {
    expect(priceFor("quickcheck", { tier: "premium" }).amount).toBe(0.5);
    expect(priceFor("whalealerts", {}).amount).toBe(10);
  });
  it("tier-driven offering uses tier when override missing", () => {
    expect(priceFor("walletprofiler", { tier: "basic" }).amount).toBe(1);
    expect(priceFor("walletprofiler", { tier: "premium" }).amount).toBe(5);
    expect(priceFor("walletprofiler", {}).amount).toBe(2);
  });
  it("unknown offering falls through to standard", () => {
    expect(priceFor("notARealOffering", {}).amount).toBe(2);
  });
  it("returns USDC symbol", () => {
    expect(priceFor("walletprofiler", {}).token).toBe("USDC");
  });
});
