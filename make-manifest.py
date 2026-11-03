"""Writes dist/repo/manifest.json, the plugin catalogue file Jellyfin reads.
Usage: python3 make-manifest.py <download-base-url>"""
import hashlib, json, os, sys, datetime
base = sys.argv[1].rstrip('/')
repo = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'dist', 'repo')
meta = json.load(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'meta.json')))
now = datetime.datetime.now(datetime.timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ')
versions = []
for line in open(os.path.join(repo, 'builds.txt')):
    ver, abi, zipname = line.split()
    md5 = hashlib.md5(open(os.path.join(repo, zipname), 'rb').read()).hexdigest()
    versions.append({"version": ver, "changelog": meta["changelog"], "targetAbi": abi,
                     "sourceUrl": f"{base}/{zipname}", "checksum": md5, "timestamp": now})
versions.sort(key=lambda v: [int(x) for x in v["version"].split('.')], reverse=True)
manifest = [{"guid": meta["guid"], "name": meta["name"], "description": meta["description"],
             "overview": meta["overview"], "owner": meta["owner"], "category": meta["category"],
             "imageUrl": "", "versions": versions}]
json.dump(manifest, open(os.path.join(repo, 'manifest.json'), 'w'), indent=2)
print(json.dumps(manifest, indent=2))
