const WMS_CACHE_VERSION = 'wms-static-v1';
const WMS_STATIC_CACHE = `warecommand-${WMS_CACHE_VERSION}`;
const WMS_PRECACHE = [
  '/offline.html',
  '/manifest.webmanifest',
  '/css/site.css',
  '/js/site.js',
  '/lib/bootstrap/dist/css/bootstrap.min.css',
  '/lib/bootstrap/dist/css/bootstrap.rtl.min.css',
  '/lib/bootstrap/dist/js/bootstrap.bundle.min.js',
  '/lib/jquery/dist/jquery.min.js',
  '/icons/warecommand.svg',
  '/favicon.ico'
];

self.addEventListener('install', function (event) {
  event.waitUntil(
    caches.open(WMS_STATIC_CACHE)
      .then(function (cache) {
        return cache.addAll(WMS_PRECACHE);
      })
  );
});

self.addEventListener('activate', function (event) {
  event.waitUntil(
    caches.keys().then(function (cacheNames) {
      return Promise.all(cacheNames
        .filter(function (cacheName) {
          return cacheName.startsWith('warecommand-') && cacheName !== WMS_STATIC_CACHE;
        })
        .map(function (cacheName) {
          return caches.delete(cacheName);
        }));
    })
  );
});

self.addEventListener('message', function (event) {
  if (event.data?.type === 'SKIP_WAITING') {
    self.skipWaiting();
  }
});

function isSameOriginStaticAsset(request, url) {
  if (request.method !== 'GET' || url.origin !== self.location.origin) {
    return false;
  }

  if (url.pathname === '/offline.html' || url.pathname === '/manifest.webmanifest' ||
      url.pathname === '/favicon.ico' || url.pathname === '/icons/warecommand.svg') {
    return true;
  }

  return ['/css/', '/js/', '/lib/'].some(function (prefix) {
    return url.pathname.startsWith(prefix);
  });
}

self.addEventListener('fetch', function (event) {
  const request = event.request;
  const url = new URL(request.url);

  if (request.mode === 'navigate' && url.origin === self.location.origin) {
    event.respondWith(
      fetch(request).catch(function () {
        return caches.match('/offline.html');
      })
    );
    return;
  }

  if (!isSameOriginStaticAsset(request, url)) {
    return;
  }

  event.respondWith(
    caches.match(request).then(function (cachedResponse) {
      const networkResponse = fetch(request).then(function (response) {
        if (response.ok) {
          const responseToCache = response.clone();
          caches.open(WMS_STATIC_CACHE).then(function (cache) {
            cache.put(request, responseToCache);
          });
        }
        return response;
      });

      return cachedResponse || networkResponse;
    })
  );
});
