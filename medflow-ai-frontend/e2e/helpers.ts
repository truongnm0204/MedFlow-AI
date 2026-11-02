import { execFileSync, spawn, type ChildProcess } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { setTimeout as delay } from 'node:timers/promises';
import type { APIRequestContext } from '@playwright/test';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

export const FRONTEND_ROOT = path.resolve(__dirname, '..');
export const REPO_ROOT = path.resolve(FRONTEND_ROOT, '..');
export const BACKEND_ROOT = path.resolve(REPO_ROOT, 'medflow-ai-backend');
export const BACKEND_BASE_URL = process.env.MEDFLOW_E2E_API_URL ?? 'http://localhost:5168';
const PID_FILE = path.resolve(__dirname, '.backend-pid');

const MIGRATION_FILES = [
  'database/migrations/002_v41_priority1_schema.sql',
  'database/migrations/003_v41_priority1_seed.sql',
  'database/migrations/004_readd_auth_verification_challenges.sql',
  'database/migrations/005_readd_auth_session_and_audit_cols.sql',
  'database/migrations/006_seed_upcoming_examination_slots.sql',
  'database/migrations/007_add_appointments_reason_for_visit.sql',
  'database/migrations/008_add_appointments_cancellation_fields.sql',
];

export interface DbConfig {
  host: string;
  port: number;
  database: string;
  user: string;
  password: string;
  containerName: string;
  hasHostPsql: boolean;
}

export interface PatientAuthTokens {
  accessToken: string;
  refreshToken: string;
  user: {
    id: number;
    email: string;
    role: string;
    status: string;
  };
}

function commandSucceeds(cmd: string, args: string[]): boolean {
  try {
    execFileSync(cmd, args, { stdio: 'ignore' });
    return true;
  } catch {
    return false;
  }
}

export function resolveDbConfig(): DbConfig {
  const containerName = process.env.MEDFLOW_E2E_PG_CONTAINER ?? 'medflow-postgres';
  const hasHostPsql = commandSucceeds('psql', ['--version']);

  let detectedPort = 5432;
  let detectedPassword = 'postgres';

  try {
    const portOutput = execFileSync('docker', ['port', containerName, '5432/tcp'], {
      encoding: 'utf8',
      stdio: ['ignore', 'pipe', 'ignore'],
    }).trim();
    const match = portOutput.match(/:(\d+)\s*$/m);
    if (match) {
      detectedPort = Number(match[1]);
    }

    const envOutput = execFileSync(
      'docker',
      ['inspect', containerName, '--format', '{{range .Config.Env}}{{println .}}{{end}}'],
      { encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] },
    );
    const passMatch = envOutput.match(/^POSTGRES_PASSWORD=(.+)$/m);
    if (passMatch) {
      detectedPassword = passMatch[1].trim();
    }
  } catch {
    // Container may not be started yet; defaults apply.
  }

  const port = process.env.MEDFLOW_E2E_DB_PORT
    ? Number(process.env.MEDFLOW_E2E_DB_PORT)
    : detectedPort;

  const password = process.env.MEDFLOW_E2E_DB_PASSWORD ?? detectedPassword;

  return {
    host: 'localhost',
    port,
    database: 'medflow_test',
    user: 'postgres',
    password,
    containerName,
    hasHostPsql,
  };
}

function runPsqlQuery(cfg: DbConfig, database: string, sql: string): string {
  if (cfg.hasHostPsql) {
    const uri = `postgresql://${cfg.user}:${cfg.password}@${cfg.host}:${cfg.port}/${database}`;
    return execFileSync('psql', [uri, '-X', '-t', '-A', '-c', sql], {
      encoding: 'utf8',
    }).trim();
  }

  return execFileSync(
    'docker',
    [
      'exec',
      '-i',
      cfg.containerName,
      'psql',
      '-U',
      cfg.user,
      '-d',
      database,
      '-X',
      '-t',
      '-A',
      '-c',
      sql,
    ],
    { encoding: 'utf8' },
  ).trim();
}

function runPsqlFile(cfg: DbConfig, database: string, filePath: string): void {
  if (cfg.hasHostPsql) {
    const uri = `postgresql://${cfg.user}:${cfg.password}@${cfg.host}:${cfg.port}/${database}`;
    execFileSync('psql', [uri, '-X', '-v', 'ON_ERROR_STOP=1', '-q', '-f', filePath], {
      stdio: 'inherit',
    });
    return;
  }

  const sqlContent = fs.readFileSync(filePath, 'utf8');
  execFileSync(
    'docker',
    [
      'exec',
      '-i',
      cfg.containerName,
      'psql',
      '-U',
      cfg.user,
      '-d',
      database,
      '-X',
      '-v',
      'ON_ERROR_STOP=1',
      '-q',
    ],
    { input: sqlContent, stdio: ['pipe', 'inherit', 'inherit'] },
  );
}

function cleanupTestBookings(cfg: DbConfig, database: string): void {
  const exists = runPsqlQuery(
    cfg,
    'postgres',
    `SELECT count(*) FROM pg_database WHERE datname = '${database}'`,
  );
  if (exists !== '1') return;

  const tableCount = Number(
    runPsqlQuery(
      cfg,
      database,
      "SELECT count(*) FROM information_schema.tables WHERE table_schema NOT IN ('pg_catalog','information_schema')",
    ),
  );
  if (tableCount === 0) return;

  // Ensure upcoming slots are seeded (idempotent) and remove leftover active online bookings for patient01 (user_id=1, id > 1)
  runPsqlFile(
    cfg,
    database,
    path.resolve(REPO_ROOT, 'database/migrations/006_seed_upcoming_examination_slots.sql'),
  );
  runPsqlQuery(cfg, database, 'DELETE FROM appointments WHERE booked_by_user_id = 1 AND id > 1;');
}

export async function ensureTestDatabase(): Promise<DbConfig> {
  let cfg = resolveDbConfig();

  const isContainerRunning = commandSucceeds('docker', [
    'inspect',
    '-f',
    '{{.State.Running}}',
    cfg.containerName,
  ]);
  if (!isContainerRunning) {
    execFileSync('docker', ['compose', 'up', '-d', 'postgres'], {
      cwd: REPO_ROOT,
      stdio: 'inherit',
    });
    cfg = resolveDbConfig();
  }

  // Wait for Postgres readiness
  for (let i = 0; i < 20; i++) {
    try {
      const ready = runPsqlQuery(cfg, 'postgres', 'SELECT 1');
      if (ready === '1') break;
    } catch {
      await delay(1000);
    }
  }

  const dbExists = runPsqlQuery(
    cfg,
    'postgres',
    `SELECT count(*) FROM pg_database WHERE datname = '${cfg.database}'`,
  );
  if (dbExists !== '1') {
    runPsqlQuery(cfg, 'postgres', `CREATE DATABASE ${cfg.database}`);
  }

  const existingTables = Number(
    runPsqlQuery(
      cfg,
      cfg.database,
      "SELECT count(*) FROM information_schema.tables WHERE table_schema NOT IN ('pg_catalog','information_schema')",
    ),
  );

  if (existingTables === 0) {
    const uri = `postgresql://${cfg.user}:${cfg.password}@${cfg.host}:${cfg.port}/${cfg.database}`;
    if (cfg.hasHostPsql && commandSucceeds('bash', ['--version'])) {
      execFileSync('bash', ['scripts/db/init-test-db.sh', uri], {
        cwd: REPO_ROOT,
        stdio: 'inherit',
      });
    } else {
      for (const relPath of MIGRATION_FILES) {
        runPsqlFile(cfg, cfg.database, path.resolve(REPO_ROOT, relPath));
      }
    }
  }

  cleanupTestBookings(cfg, cfg.database);
  cleanupTestBookings(cfg, 'medflow_db');

  return cfg;
}

async function isBackendHealthy(): Promise<boolean> {
  try {
    const res = await fetch(`${BACKEND_BASE_URL}/api/v1/hospitals`);
    return res.ok;
  } catch {
    return false;
  }
}

export async function startBackend(cfg: DbConfig): Promise<{ child: ChildProcess | null }> {
  if (!process.env.GITHUB_ACTIONS && (await isBackendHealthy())) {
    if (fs.existsSync(PID_FILE)) {
      fs.unlinkSync(PID_FILE);
    }
    return { child: null };
  }

  const connectionString = `Host=${cfg.host};Port=${cfg.port};Database=${cfg.database};Username=${cfg.user};Password=${cfg.password}`;

  const child = spawn('dotnet', ['run', '--project', 'src/MedFlow.API', '--no-launch-profile'], {
    cwd: BACKEND_ROOT,
    env: {
      ...process.env,
      ASPNETCORE_ENVIRONMENT: 'Test',
      ASPNETCORE_URLS: BACKEND_BASE_URL,
      ConnectionStrings__DefaultConnection: connectionString,
      Jwt__Secret: 'IntegrationTest_JwtSecret_AtLeast32Chars_2026',
      Jwt__Issuer: 'MedFlow',
      Jwt__Audience: 'MedFlow',
      Smtp__Host: 'localhost',
    },
    stdio: 'ignore',
    detached: false,
  });

  if (child.pid) {
    fs.writeFileSync(PID_FILE, String(child.pid), 'utf8');
  }

  const deadline = Date.now() + 60_000;
  while (Date.now() < deadline) {
    if (await isBackendHealthy()) {
      return { child };
    }
    await delay(1000);
  }

  stopBackend();
  throw new Error(`Timed out waiting for backend at ${BACKEND_BASE_URL}/api/v1/hospitals`);
}

export function stopBackend(): void {
  if (!fs.existsSync(PID_FILE)) return;

  const pidText = fs.readFileSync(PID_FILE, 'utf8').trim();
  fs.unlinkSync(PID_FILE);

  const pid = Number(pidText);
  if (!Number.isInteger(pid) || pid <= 0) return;

  try {
    if (process.platform === 'win32') {
      execFileSync('taskkill', ['/PID', String(pid), '/T', '/F'], { stdio: 'ignore' });
    } else {
      process.kill(pid, 'SIGTERM');
    }
  } catch {
    // Process already exited.
  }
}

export function countBookedAppointmentsForPatient01(): number {
  const cfg = resolveDbConfig();
  let total = 0;

  for (const dbName of [cfg.database, 'medflow_db']) {
    try {
      const exists = runPsqlQuery(
        cfg,
        'postgres',
        `SELECT count(*) FROM pg_database WHERE datname = '${dbName}'`,
      );
      if (exists === '1') {
        const count = Number(
          runPsqlQuery(
            cfg,
            dbName,
            'SELECT count(*) FROM appointments WHERE booked_by_user_id = 1',
          ),
        );
        if (Number.isFinite(count)) {
          total += count;
        }
      }
    } catch {
      // Ignore if database does not exist or has no schema.
    }
  }

  return total;
}

export async function loginPatient(request: APIRequestContext): Promise<PatientAuthTokens> {
  const response = await request.post(`${BACKEND_BASE_URL}/api/v1/auth/login`, {
    data: {
      email: 'patient01@medflow.test',
      password: 'Demo@12345',
    },
  });

  if (!response.ok()) {
    const body = await response.text();
    throw new Error(`loginPatient failed with status ${response.status()}: ${body}`);
  }

  const data = (await response.json()) as PatientAuthTokens;
  return {
    accessToken: data.accessToken,
    refreshToken: data.refreshToken,
    user: data.user,
  };
}
