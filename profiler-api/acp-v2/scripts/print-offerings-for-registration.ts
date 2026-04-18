import { OFFERINGS } from "../src/offerings/registry.js";
import { priceFor } from "../src/pricing.js";

function main() {
  for (const offering of Object.values(OFFERINGS)) {
    const basePrice = priceFor(offering.name, {});
    console.log("=".repeat(72));
    console.log(`Offering: ${offering.name}`);
    console.log(`Price (default / standard tier): ${basePrice.amount} USDC`);
    console.log("");
    console.log("Description:");
    console.log(offering.description);
    console.log("");
    console.log("Requirement schema:");
    console.log(JSON.stringify(offering.requirementSchema, null, 2));
    console.log("");
  }
}

main();
