const { env } = require('process');

// Every API call goes through the LankaMart gateway (YARP). Aspire injects its address;
// without Aspire, fall back to the fixed port from docs/build-guide.md Appendix A.
const target = env["services__gateway__http__0"] ?? 'http://localhost:5100';

const PROXY_CONFIG = [
  {
    context: ["/api"],
    target,
    secure: false,
    changeOrigin: true
  }
]

module.exports = PROXY_CONFIG;
