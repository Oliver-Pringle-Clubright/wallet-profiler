import { describe, it, expect } from "vitest";
import { OFFERINGS } from "../src/offerings/registry.js";

describe("registry", () => {
  it("exports exactly 22 offerings", () => {
    expect(Object.keys(OFFERINGS)).toHaveLength(22);
  });
  it("every offering has name matching its key", () => {
    for (const [key, value] of Object.entries(OFFERINGS)) {
      expect(value.name).toBe(key);
    }
  });
});
