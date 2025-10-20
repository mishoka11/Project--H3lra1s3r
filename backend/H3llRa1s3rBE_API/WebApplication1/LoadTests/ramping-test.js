import http from 'k6/http';
import { sleep } from 'k6';

export const options = {
    stages: [
        { duration: '1m', target: 20 },
        { duration: '2m', target: 100 },
        { duration: '1m', target: 0 },
    ],
};

export default function () {
    http.get('http://catalog-service:8080/api/v1/catalog');
    sleep(1);
}
