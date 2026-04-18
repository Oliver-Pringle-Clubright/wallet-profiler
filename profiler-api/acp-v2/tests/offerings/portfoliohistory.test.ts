// tests/offerings/portfoliohistory.test.ts
import { describe, it, expect } from "vitest";
import { portfoliohistory } from "../../src/offerings/portfoliohistory.js";

const A = "0x" + "a".repeat(40);

describe("portfoliohistory", () => {
  it("rejects hours < 1", () => {
    expect(portfoliohistory.validate({ address: A, hours: 0 }).valid).toBe(false);
  });
  it("rejects hours > 168", () => {
    expect(portfoliohistory.validate({ address: A, hours: 169 }).valid).toBe(false);
  });
  it("accepts hours 24", () => {
    expect(portfoliohistory.validate({ address: A, hours: 24 }).valid).toBe(true);
  });
});
