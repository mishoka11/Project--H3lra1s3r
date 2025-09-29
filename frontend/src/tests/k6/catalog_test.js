import http from 'k6/http';
import { check, sleep } from 'k6';

export let options = {
  vus: 50, // small-scale for now
  duration: '30s',
  thresholds: {
    http_req_duration: ['p(95)<200'], // 95% under 200ms
    http_req_failed: ['rate<0.005'], // <0.5% errors
  },
};

export default function () {
  let res = http.get('http://localhost:8080/catalog');
  
  check(res, {
    'status is 200': (r) => r.status === 200,
    'response time OK': (r) => r.timings.duration < 200,
  });

  sleep(1);
}
