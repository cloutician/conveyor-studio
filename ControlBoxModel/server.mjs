import http from 'node:http';
import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.dirname(fileURLToPath(import.meta.url));
const port = Number(process.env.CONTROL_BOX_PORT || 5173);
const types = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.glb': 'model/gltf-binary', '.png': 'image/png', '.svg': 'image/svg+xml' };

http.createServer(async (request, response) => {
  try {
    const url = new URL(request.url, `http://127.0.0.1:${port}`);
    const name = decodeURIComponent(url.pathname);
    const vendor = name.startsWith('/vendor/three/');
    const base = vendor ? path.join(root, 'node_modules', 'three') : root;
    const relative = vendor ? name.slice('/vendor/three/'.length) : name === '/' ? 'index.html' : name.slice(1);
    const target = path.resolve(base, relative);
    const checked = path.relative(base, target);
    if (checked.startsWith('..') || path.isAbsolute(checked)) {
      response.writeHead(403).end('Forbidden');
      return;
    }
    const contents = await fs.readFile(target);
    response.writeHead(200, { 'Content-Type': types[path.extname(target)] || 'application/octet-stream', 'Cache-Control': 'no-store' });
    response.end(contents);
  } catch {
    response.writeHead(404).end('Not found');
  }
}).listen(port, '127.0.0.1', () => {
  console.log(`Control Box Inspector: http://127.0.0.1:${port}`);
});
