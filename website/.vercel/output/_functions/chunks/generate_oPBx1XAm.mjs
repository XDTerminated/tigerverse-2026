import { c as createImageTo3dTask } from './meshy_BXukJfvK.mjs';

const prerender = false;
const POST = async ({ request }) => {
  let body;
  try {
    body = await request.json();
  } catch {
    return Response.json({ error: "Invalid JSON" }, { status: 400 });
  }
  const { imageDataUri, name } = body;
  if (!imageDataUri || !imageDataUri.startsWith("data:image/")) {
    return Response.json({ error: "imageDataUri (data URI) required" }, { status: 400 });
  }
  if (!name || typeof name !== "string") {
    return Response.json({ error: "name required" }, { status: 400 });
  }
  try {
    const taskId = await createImageTo3dTask(imageDataUri);
    return Response.json({ taskId, name });
  } catch (err) {
    return Response.json({ error: err.message }, { status: 502 });
  }
};

const _page = /*#__PURE__*/Object.freeze(/*#__PURE__*/Object.defineProperty({
  __proto__: null,
  POST,
  prerender
}, Symbol.toStringTag, { value: 'Module' }));

const page = () => _page;

export { page };
