import type { ValidationResult } from "../validators.js";
import type { ProfilerClient } from "../profilerClient.js";

export interface OfferingContext {
  client: ProfilerClient;
}

export interface Offering {
  name: string;
  description: string;
  requirementSchema: Record<string, unknown>;
  validate(req: Record<string, unknown>): ValidationResult;
  execute(req: Record<string, unknown>, ctx: OfferingContext): Promise<unknown>;
}
