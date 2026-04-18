import type { ProfilerClient } from "./profilerClient.js";

export const INLINE_SIZE_LIMIT_BYTES = 50_000;

export async function toDeliverable(
  jobId: string,
  payload: unknown,
  client: ProfilerClient
): Promise<string> {
  const json = JSON.stringify(payload);
  if (json.length <= INLINE_SIZE_LIMIT_BYTES) return json;

  try {
    const { url } = await client.storeDeliverable(jobId, payload);
    return url;
  } catch (err) {
    console.warn(
      `[deliverable] storeDeliverable failed for job ${jobId}, falling back to inline submit: ${String(err)}`
    );
    return json;
  }
}
