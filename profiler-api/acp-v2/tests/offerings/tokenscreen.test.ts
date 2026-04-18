// tests/offerings/tokenscreen.test.ts
import { describe, it, expect } from "vitest";
import { tokenscreen } from "../../src/offerings/tokenscreen.js";

const CONTRACT = "0x" + "c".repeat(40);

describe("tokenscreen", () => {
  it("rejects limit below 1", () => {
    expect(tokenscreen.validate({ contract: CONTRACT, limit: 0 }).valid).toBe(false);
  });
  it("rejects limit above 100", () => {
    expect(tokenscreen.validate({ contract: CONTRACT, limit: 101 }).valid).toBe(false);
  });
  it("accepts limit 50", () => {
    expect(tokenscreen.validate({ contract: CONTRACT, limit: 50 }).valid).toBe(true);
  });
});
