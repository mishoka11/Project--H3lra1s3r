import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
    stages: [
        { duration: '30s', target: 10 },
        { duration: '1m', target: 50 },
        { duration: '30s', target: 0 },
    ],
    thresholds: {
        http_req_failed: ['rate<0.01'],
        http_req_duration: ['p(95)<500'],
    },
};

export default function () {
    const catalog = http.get('http://aa52acadb1ab14532bafc2296357a742-1919469305.eu-central-1.elb.amazonaws.com:8080/api/v1/catalog');
    const design = http.get('http://af32dd9bd8f1244bebe6a6a482d22600-952858228.eu-central-1.elb.amazonaws.com:8080/api/v1/designs/123');
    const order = http.get('http://<ADD-ORDER-SERVICE-ELB-HERE>:8080/api/v1/orders/1');

    console.log(
        `Catalog: ${catalog.status}, Design: ${design.status}, Order: ${order.status}`
    );

    check(catalog, { 'catalog ok': (r) => r.status === 200 });
    check(design, { 'design ok': (r) => r.status === 200 || r.status === 404 });
    check(order, { 'order ok': (r) => r.status === 200 || r.status === 404 });

    sleep(1);
}
