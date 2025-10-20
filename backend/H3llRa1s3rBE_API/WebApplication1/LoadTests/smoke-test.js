import http from 'k6/http';
import { sleep, check } from 'k6';

export let options = {
    vus: 50, // virtual users
    duration: '30s',
    thresholds: {
        http_req_duration: ['p(95)<400'], // 95% of requests under 400ms
    },
};

export default function () {
    const baseUrl = 'http://catalog-service:8080';
    let res = http.get(`${baseUrl}/api/v1/catalog`);
    check(res, { 'status is 200': (r) => r.status === 200 });
    sleep(1);
}
