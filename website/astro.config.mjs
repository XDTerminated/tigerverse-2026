// @ts-check
import { defineConfig, envField } from 'astro/config';
import react from '@astrojs/react';
import vercel from '@astrojs/vercel';
import tailwindcss from '@tailwindcss/vite';

// https://astro.build/config
export default defineConfig({
  output: 'server',
  adapter: vercel(),
  integrations: [react()],
  vite: {
    plugins: [tailwindcss()],
  },
  env: {
    schema: {
      MESHY_API_KEY: envField.string({ context: 'server', access: 'secret' }),
      UPLOADTHING_TOKEN: envField.string({ context: 'server', access: 'secret' }),
    },
  },
});
