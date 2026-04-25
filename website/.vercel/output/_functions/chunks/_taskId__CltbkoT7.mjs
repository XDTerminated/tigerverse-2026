import { U as UPLOADTHING_TOKEN, g as getTask } from './meshy_BXukJfvK.mjs';
import { UTFile, UTApi } from 'uploadthing/server';

const utapi = new UTApi({ token: UPLOADTHING_TOKEN });
async function uploadGlbFromUrl(sourceUrl, name) {
  const sourceRes = await fetch(sourceUrl);
  if (!sourceRes.ok) {
    throw new Error(`Failed to fetch source GLB: ${sourceRes.status}`);
  }
  const buffer = await sourceRes.arrayBuffer();
  const safeName = `${name.replace(/[^a-z0-9-_]+/gi, "_").slice(0, 64) || "model"}.glb`;
  const file = new UTFile([buffer], safeName, { type: "model/gltf-binary" });
  const result = await utapi.uploadFiles(file);
  if (result.error) {
    throw new Error(`UploadThing failed: ${result.error.message}`);
  }
  return result.data.ufsUrl;
}

const prerender = false;
const GET = async ({ params, url }) => {
  const taskId = params.taskId;
  if (!taskId) {
    return Response.json({ error: "taskId required" }, { status: 400 });
  }
  const name = url.searchParams.get("name") ?? "model";
  try {
    const task = await getTask(taskId);
    if (task.status === "SUCCEEDED") {
      const glbUrl = task.model_urls?.glb;
      if (!glbUrl) {
        return Response.json(
          { status: "FAILED", error: "Meshy returned no GLB URL" },
          { status: 502 }
        );
      }
      const permanentUrl = await uploadGlbFromUrl(glbUrl, name);
      return Response.json({
        status: "SUCCEEDED",
        progress: 100,
        modelUrl: permanentUrl,
        thumbnailUrl: task.thumbnail_url
      });
    }
    if (task.status === "FAILED" || task.status === "CANCELED" || task.status === "EXPIRED") {
      return Response.json({
        status: task.status,
        error: task.task_error?.message ?? task.status
      });
    }
    return Response.json({
      status: task.status,
      progress: task.progress ?? 0
    });
  } catch (err) {
    return Response.json({ error: err.message }, { status: 502 });
  }
};

const _page = /*#__PURE__*/Object.freeze(/*#__PURE__*/Object.defineProperty({
  __proto__: null,
  GET,
  prerender
}, Symbol.toStringTag, { value: 'Module' }));

const page = () => _page;

export { page };
