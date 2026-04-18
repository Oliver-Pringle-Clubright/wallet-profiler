import { describe, it, expect } from "vitest";
import { getChain } from "../src/chain.js";
import { base, baseSepolia } from "viem/chains";

describe("getChain", () => {
  it("returns viem base for 'base'", () => {
    expect(getChain("base")).toBe(base);
  });
  it("returns viem baseSepolia for 'baseSepolia'", () => {
    expect(getChain("baseSepolia")).toBe(baseSepolia);
  });
});
