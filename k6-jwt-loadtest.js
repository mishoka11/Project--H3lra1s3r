import http from 'k6/http';
import { check, sleep } from 'k6';

// ----------------------------
// Scenario config
// ----------------------------
export const options = {
  stages: [
    // RAMP-UP
    { duration: '20s', target: 10 },
    { duration: '20s', target: 20 },
    { duration: '30s', target: 30 }, // PEAK
    // RAMP-DOWN
    { duration: '20s', target: 10 },
    { duration: '10s', target: 0 },
  ],
  thresholds: {
    http_req_duration: ['p(95)<500'], // 95% under 500ms
    http_req_failed: ['rate<0.01'],   // <1% failures
  },
};

// Static JWT signed with your cluster secret (HS256, key = h3llra1s3r-very-secret-jwt-key)
const JWT =
  'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJsb2FkdGVzdC11c2VyIiwibmFtZSI6IkxvYWQgVGVzdGVyIiwicm9sZSI6IlVzZXIiLCJleHAiOjIwNTEyMjI0MDB9.vCMa61gWhCmqo1jjTqYn2gQJ_06HXXNGEjMBNff9Oi0';

// Base URL for catalog-service inside the cluster
const BASE_URL =
  __ENV.TARGET_URL ||
  'http://catalog-service.h3llra1s3r.svc.cluster.local:8080';

export default function () {
  const res = http.get(`${BASE_URL}/api/v1/catalog`, {
    headers: {
      Authorization: `Bearer ${JWT}`,
    },
  });

  check(res, {
    'status is 200': (r) => r.status === 200,
  });

  // Short pause to avoid insane hammering
  sleep(1);
}
