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
    // sharp is a native Node module used by lib/imagePreprocess; it must
    // load at runtime instead of being bundled into the SSR output.
    ssr: { external: ['sharp'] },
  },
  env: {
    schema: {
      MESHY_API_KEY: envField.string({ context: 'server', access: 'secret' }),
      UPLOADTHING_TOKEN: envField.string({ context: 'server', access: 'secret' }),
      ELEVENLABS_API_KEY: envField.string({ context: 'server', access: 'secret', optional: true }),
    },
  },
});
