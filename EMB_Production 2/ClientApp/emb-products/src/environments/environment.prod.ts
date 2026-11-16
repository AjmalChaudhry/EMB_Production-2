export const environment = {
  production: true,
  // The Angular build is served by the same MVC app it calls (Content/emb-products),
  // so API calls should be same-origin relative requests, not a hardcoded dev host.
  apiUrl: ''
};
