FROM node:22-slim

WORKDIR /app

# Install the ACP CLI
RUN npm install -g @anthropic-ai/acp 2>/dev/null || echo "ACP CLI install will be done manually"

COPY package.json ./
RUN npm install

COPY offering.json handlers.ts ./

ENV PROFILER_API_URL=http://profiler-api:5000

CMD ["npx", "acp", "serve", "start"]
