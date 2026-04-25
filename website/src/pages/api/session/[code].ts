import type { APIRoute } from 'astro';
import { getSession } from '../../../lib/sessions';

export const prerender = false;

// Quest polls this every 3s. Returns both players' state.
export const GET: APIRoute = async ({ params }) => {
  const code = params.code;
  if (!code) {
    return Response.json({ error: 'code required' }, { status: 400 });
  }

  const session = await getSession(code);

  // Map our internal status enum to the strings the Unity client expects.
  // Unity's SessionApiClient.SessionData expects: queued|generating|rigging|cry|ready|error
  // 'empty' just means nobody's drawn for this slot yet — surface as 'queued'.
  const mapStatus = (s: string) => (s === 'empty' ? 'queued' : s);

  return Response.json({
    code: session.code,
    p1: {
      status: mapStatus(session.p1.status),
      name: session.p1.name,
      imageUrl: session.p1.imageUrl,
      glbUrl: session.p1.glbUrl,
      cryUrl: session.p1.cryUrl,
      stats: session.p1.stats,
    },
    p2: {
      status: mapStatus(session.p2.status),
      name: session.p2.name,
      imageUrl: session.p2.imageUrl,
      glbUrl: session.p2.glbUrl,
      cryUrl: session.p2.cryUrl,
      stats: session.p2.stats,
    },
  });
};
