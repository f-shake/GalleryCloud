#!/usr/bin/env bash
cd "$(dirname "$0")/src/GalleryCloud.Web"
[ ! -d node_modules ] && npm install
npm run dev
