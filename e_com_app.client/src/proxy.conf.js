const { env } = require('process');

const target = env["services__e_com_app-server__https__0"] ?? 'https://localhost:7168';

const PROXY_CONFIG = [
  {
    context: [
      "/weatherforecast",
    ],
    target,
    secure: false
  }
]

module.exports = PROXY_CONFIG;
