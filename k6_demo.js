import http from 'k6/http';
import { check, sleep } from 'k6';

export let options = {
  vus: 10,
  duration: '20s',
  thresholds: {
    http_req_failed: ['rate<0.01'],
    http_req_duration: ['p(95)<400']
  }
};

export default function () {
  const endpoints = [
    'http://catalog-service:8080/api/v1/catalog',
    'http://design-service:8080/api/v1/designs',
    'http://order-service:8080/api/v1/orders'
  ];
  for (const url of endpoints) {
    const res = http.get(url);
    check(res, { 'status 200': (r) => r.status === 200 });
  }
  sleep(0.5);
}
