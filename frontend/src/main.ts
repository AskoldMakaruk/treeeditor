import { mount } from 'svelte';
import App from './App.svelte';
import './app.css';

const target = document.getElementById('app');
if (!target) {
  throw new Error('Element #app was not found.');
}

const app = mount(App, { target });

export default app;
