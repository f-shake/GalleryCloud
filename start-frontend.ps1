try { Push-Location src\GalleryCloud.Web; if (-not (Test-Path node_modules)) { npm install }; npm run dev -- --host }
finally { Pop-Location }
