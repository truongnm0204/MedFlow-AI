import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { StrictMode, useContext, useEffect, useLayoutEffect, useState } from 'react';
import {
  BrowserRouter,
  Link,
  MemoryRouter,
  Route,
  Routes,
  UNSAFE_NavigationContext,
  useNavigate,
} from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { useAuthStore } from '@/features/auth/store/auth.store';
import {
  buildAdminDraftStorageKey,
  clearAllAdminDrafts,
  loadAdminDraft,
  saveAdminDraft,
} from '@/shared/lib/admin-draft-storage';
import { resetNavigationGuardManagerForTests } from '@/shared/lib/navigation-guard-manager';

import { UnsavedChangesModal, useUnsavedChanges } from '../use-unsaved-changes';

function HarnessForm({
  initialDirty = false,
  withEscapeListener = false,
}: {
  initialDirty?: boolean;
  withEscapeListener?: boolean;
}) {
  const navigate = useNavigate();
  const [text, setText] = useState(initialDirty ? 'Dữ liệu đang gõ' : '');
  const [savedBaseline, setSavedBaseline] = useState('');
  const isDirty = text !== savedBaseline;

  const { isBlocked, confirmNavigation, cancelNavigation, allowNavigation, guardAction } =
    useUnsavedChanges(isDirty);

  useEffect(() => {
    if (!withEscapeListener) return;
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        guardAction(() => navigate('/list'));
      }
    };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [guardAction, navigate, withEscapeListener]);

  return (
    <div>
      <h1>Trang Biểu Mẫu</h1>
      <label htmlFor="demo-input">Ghi chú</label>
      <input id="demo-input" value={text} onChange={(e) => setText(e.target.value)} />
      <Link to="/sidebar-target">Đi tới Sidebar</Link>
      <button type="button" onClick={() => guardAction(() => navigate('/list'))}>
        Hủy qua guardAction
      </button>
      <button type="button" onClick={() => navigate('/replace-target', { replace: true })}>
        Chuyển bằng Replace
      </button>
      <button
        type="button"
        onClick={() => {
          allowNavigation();
          navigate('/list');
        }}
      >
        Lưu thành công 1 lần
      </button>
      <button
        type="button"
        onClick={() => {
          allowNavigation();
          // Intentionally do NOT navigate here to test single-shot microtask expiry
        }}
      >
        Cấp quyền nhưng không điều hướng
      </button>
      <button type="button" onClick={() => setSavedBaseline(text)}>
        Đặt mốc sạch
      </button>

      <UnsavedChangesModal open={isBlocked} onStay={cancelNavigation} onLeave={confirmNavigation} />
    </div>
  );
}

describe('useUnsavedChanges & centralized navigation-guard-manager', () => {
  beforeEach(() => {
    resetNavigationGuardManagerForTests();
    localStorage.clear();
    sessionStorage.clear();
    useAuthStore.setState({
      user: null,
      accessToken: null,
      refreshToken: null,
      isAuthenticated: false,
      sessionEpoch: 0,
    });
    window.history.replaceState({ idx: 0 }, '', '/form');
  });

  afterEach(() => {
    resetNavigationGuardManagerForTests();
    vi.restoreAllMocks();
  });

  it('triggers native beforeunload only when dirty and suppresses it when clean', () => {
    render(
      <MemoryRouter initialEntries={['/form']}>
        <Routes>
          <Route path="/form" element={<HarnessForm />} />
        </Routes>
      </MemoryRouter>,
    );

    const cleanEvent = new Event('beforeunload', { cancelable: true });
    window.dispatchEvent(cleanEvent);
    expect(cleanEvent.defaultPrevented).toBe(false);

    fireEvent.change(screen.getByLabelText('Ghi chú'), { target: { value: 'Thay đổi mới' } });

    const dirtyEvent = new Event('beforeunload', { cancelable: true });
    window.dispatchEvent(dirtyEvent);
    expect(dirtyEvent.defaultPrevented).toBe(true);
  });

  it('blocks PUSH and REPLACE on a real BrowserRouter, focuses Stay button, traps Tab, handles Escape without re-opening, and restores focus', async () => {
    render(
      <BrowserRouter>
        <Routes>
          <Route path="/form" element={<HarnessForm withEscapeListener />} />
          <Route path="/sidebar-target" element={<div>Màn hình Sidebar</div>} />
          <Route path="/replace-target" element={<div>Màn hình Replace</div>} />
          <Route path="/list" element={<div>Màn hình Danh sách</div>} />
        </Routes>
      </BrowserRouter>,
    );

    const input = screen.getByLabelText('Ghi chú');
    input.focus();
    fireEvent.change(input, { target: { value: 'Dữ liệu quan trọng' } });

    // Trigger Link PUSH on real BrowserRouter
    fireEvent.click(screen.getByRole('link', { name: 'Đi tới Sidebar' }));

    const modal = screen.getByTestId('unsaved-changes-modal');
    expect(modal).toBeInTheDocument();
    expect(window.location.pathname).toBe('/form');

    // Default focus is on "Ở lại tiếp tục nhập"
    const stayBtn = screen.getByRole('button', { name: 'Ở lại tiếp tục nhập' });
    const leaveBtn = screen.getByRole('button', { name: 'Rời trang & hủy thay đổi' });
    expect(document.activeElement).toBe(stayBtn);

    // Focus trap: Shift+Tab from first wraps to last; Tab from last wraps to first
    fireEvent.keyDown(window, { key: 'Tab', shiftKey: true });
    expect(document.activeElement).toBe(leaveBtn);
    fireEvent.keyDown(window, { key: 'Tab', shiftKey: false });
    expect(document.activeElement).toBe(stayBtn);

    // Put focus back on input before testing Escape focus restore
    fireEvent.click(stayBtn);
    expect(screen.queryByTestId('unsaved-changes-modal')).not.toBeInTheDocument();

    input.focus();
    // Press Escape once -> page's Escape listener triggers guardAction -> opens modal
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(screen.getByTestId('unsaved-changes-modal')).toBeInTheDocument();

    // Press Escape while modal is open -> closes modal ONLY, must NOT re-trigger page Escape listener!
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(screen.queryByTestId('unsaved-changes-modal')).not.toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Trang Biểu Mẫu' })).toBeInTheDocument();
    expect(document.activeElement).toBe(input);

    // Also blocks REPLACE transitions and proceeds cleanly when confirmed
    fireEvent.click(screen.getByRole('button', { name: 'Chuyển bằng Replace' }));
    expect(screen.getByTestId('unsaved-changes-modal')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Rời trang & hủy thay đổi' }));
    expect(await screen.findByText('Màn hình Replace')).toBeInTheDocument();
  });

  it('prevents double-modal when guardAction wraps navigate() and enforces single-shot allowNavigation()', async () => {
    render(
      <BrowserRouter>
        <Routes>
          <Route path="/form" element={<HarnessForm initialDirty />} />
          <Route path="/list" element={<div>Màn hình Danh sách</div>} />
          <Route path="/sidebar-target" element={<div>Màn hình Sidebar</div>} />
        </Routes>
      </BrowserRouter>,
    );

    // Unused allowNavigation() expires on microtask and does NOT permanently disable guard
    fireEvent.click(screen.getByRole('button', { name: 'Cấp quyền nhưng không điều hướng' }));
    await Promise.resolve();

    // guardAction(() => navigate('/list')) opens modal ONCE, and confirming navigates directly without a 2nd modal
    fireEvent.click(screen.getByRole('button', { name: 'Hủy qua guardAction' }));
    expect(screen.getAllByTestId('unsaved-changes-modal')).toHaveLength(1);

    fireEvent.click(screen.getByRole('button', { name: 'Rời trang & hủy thay đổi' }));
    expect(screen.queryByTestId('unsaved-changes-modal')).not.toBeInTheDocument();
    expect(await screen.findByText('Màn hình Danh sách')).toBeInTheDocument();
  });

  it('intercepts physical Browser Back POP events via capture popstate + history.state.idx without unmounting the dirty form', async () => {
    // Seed entry 0 (/list) then push entry 1 (/form) on BrowserRouter
    window.history.replaceState({ idx: 0 }, '', '/list');
    render(
      <BrowserRouter>
        <Routes>
          <Route path="/list" element={<Link to="/form">Mở biểu mẫu</Link>} />
          <Route path="/form" element={<HarnessForm />} />
        </Routes>
      </BrowserRouter>,
    );

    fireEvent.click(screen.getByRole('link', { name: 'Mở biểu mẫu' }));
    expect(await screen.findByRole('heading', { name: 'Trang Biểu Mẫu' })).toBeInTheDocument();
    expect((window.history.state as { idx?: number }).idx).toBe(1);

    const goSpy = vi.spyOn(window.history, 'go');
    fireEvent.change(screen.getByLabelText('Ghi chú'), {
      target: { value: 'Nội dung chưa lưu khi bấm Back' },
    });

    // Simulate physical Browser Back: browser updates history.state to idx=0 and dispatches popstate
    act(() => {
      window.history.replaceState({ idx: 0 }, '', '/list');
      window.dispatchEvent(new PopStateEvent('popstate', { state: { idx: 0 } }));
    });

    // Form MUST remain mounted with typed value intact, modal opens, and history.go(+1) reverts cursor
    expect(screen.getByTestId('unsaved-changes-modal')).toBeInTheDocument();
    expect((screen.getByLabelText('Ghi chú') as HTMLInputElement).value).toBe(
      'Nội dung chưa lưu khi bấm Back',
    );
    expect(goSpy).toHaveBeenCalledWith(1);

    //Simulate the browser's revert popstate back to idx=1
    act(() => {
      window.history.replaceState({ idx: 1 }, '', '/form');
      window.dispatchEvent(new PopStateEvent('popstate', { state: { idx: 1 } }));
    });

    // User chooses Stay -> stays on /form
    fireEvent.click(screen.getByRole('button', { name: 'Ở lại tiếp tục nhập' }));
    expect(screen.queryByTestId('unsaved-changes-modal')).not.toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Trang Biểu Mẫu' })).toBeInTheDocument();

    // User presses Back again and confirms Leave -> replays history.go(-1)
    act(() => {
      window.history.replaceState({ idx: 0 }, '', '/list');
      window.dispatchEvent(new PopStateEvent('popstate', { state: { idx: 0 } }));
    });
    expect(screen.getByTestId('unsaved-changes-modal')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Rời trang & hủy thay đổi' }));

    await waitFor(() => {
      expect(goSpy).toHaveBeenCalledWith(-1);
    });
  });

  it('retains persistent global popstate interception across guard unmount/remount cycles for Browser Forward POP', async () => {
    window.history.replaceState({ idx: 0 }, '', '/form');
    render(
      <BrowserRouter>
        <Routes>
          <Route path="/form" element={<HarnessForm />} />
          <Route path="/sidebar-target" element={<Link to="/form">Quay lại biểu mẫu</Link>} />
        </Routes>
      </BrowserRouter>,
    );

    // Navigate cleanly from /form (idx=0) -> /sidebar-target (idx=1), unmounting all guards
    fireEvent.click(screen.getByRole('link', { name: 'Đi tới Sidebar' }));
    expect(await screen.findByRole('link', { name: 'Quay lại biểu mẫu' })).toBeInTheDocument();
    expect((window.history.state as { idx?: number }).idx).toBe(1);

    // Browser Back while clean back to /form (idx=0)
    act(() => {
      window.history.replaceState({ idx: 0 }, '', '/form');
      window.dispatchEvent(new PopStateEvent('popstate', { state: { idx: 0 } }));
    });
    expect(await screen.findByRole('heading', { name: 'Trang Biểu Mẫu' })).toBeInTheDocument();

    const goSpy = vi.spyOn(window.history, 'go');
    fireEvent.change(screen.getByLabelText('Ghi chú'), {
      target: { value: 'Dữ liệu nhập dở chặn Forward' },
    });

    // Browser Forward (POP delta=+1, idx=0 -> idx=1) while dirty MUST be intercepted and reverted with history.go(-1)
    act(() => {
      window.history.replaceState({ idx: 1 }, '', '/sidebar-target');
      window.dispatchEvent(new PopStateEvent('popstate', { state: { idx: 1 } }));
    });

    expect(screen.getByTestId('unsaved-changes-modal')).toBeInTheDocument();
    expect(goSpy).toHaveBeenCalledWith(-1);
  });

  it('coordinates multiple mounted guards in LIFO order and restores original navigator methods cleanly under React StrictMode', () => {
    const navigatorHolder: { ref: { push?: unknown } | null; initialPush: unknown } = {
      ref: null,
      initialPush: null,
    };

    function NavigatorInspector() {
      const ctx = useContext(UNSAFE_NavigationContext);
      useLayoutEffect(() => {
        if (ctx?.navigator && !navigatorHolder.ref) {
          navigatorHolder.ref = ctx.navigator as unknown as { push?: unknown };
          navigatorHolder.initialPush = ctx.navigator.push;
        }
      }, [ctx]);
      return null;
    }

    function DualForms({ showChild }: { showChild: boolean }) {
      const parentGuard = useUnsavedChanges(true);
      return (
        <div>
          <Link to="/away">Rời trang</Link>
          <UnsavedChangesModal
            open={parentGuard.isBlocked}
            title="Cảnh báo Form Cha"
            onStay={parentGuard.cancelNavigation}
            onLeave={parentGuard.confirmNavigation}
          />
          {showChild && <ChildModalGuard />}
        </div>
      );
    }

    function ChildModalGuard() {
      const childGuard = useUnsavedChanges(true);
      return (
        <UnsavedChangesModal
          open={childGuard.isBlocked}
          title="Cảnh báo Form Con"
          onStay={childGuard.cancelNavigation}
          onLeave={childGuard.confirmNavigation}
        />
      );
    }

    // Mount parent form first, then dynamically open child modal form on top (LIFO stack)
    const { rerender } = render(
      <StrictMode>
        <MemoryRouter initialEntries={['/']}>
          <NavigatorInspector />
          <Routes>
            <Route path="/" element={<DualForms showChild={false} />} />
            <Route path="/away" element={<div>Đã rời trang</div>} />
          </Routes>
        </MemoryRouter>
      </StrictMode>,
    );

    rerender(
      <StrictMode>
        <MemoryRouter initialEntries={['/']}>
          <NavigatorInspector />
          <Routes>
            <Route path="/" element={<DualForms showChild />} />
            <Route path="/away" element={<div>Đã rời trang</div>} />
          </Routes>
        </MemoryRouter>
      </StrictMode>,
    );

    // Clicking Link while both guards are dirty opens ONLY the topmost (Child) modal
    fireEvent.click(screen.getByRole('link', { name: 'Rời trang' }));
    expect(screen.getByText('Cảnh báo Form Con')).toBeInTheDocument();
    expect(screen.queryByText('Cảnh báo Form Cha')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Ở lại tiếp tục nhập' }));

    // Unmount Child guard -> Parent guard remains active without losing navigator wrapper
    rerender(
      <StrictMode>
        <MemoryRouter initialEntries={['/']}>
          <NavigatorInspector />
          <Routes>
            <Route path="/" element={<DualForms showChild={false} />} />
            <Route path="/away" element={<div>Đã rời trang</div>} />
          </Routes>
        </MemoryRouter>
      </StrictMode>,
    );

    fireEvent.click(screen.getByRole('link', { name: 'Rời trang' }));
    expect(screen.getByText('Cảnh báo Form Cha')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Ở lại tiếp tục nhập' }));

    // Unmount all guards -> navigator.push is restored back to exact original function
    rerender(
      <StrictMode>
        <MemoryRouter initialEntries={['/']}>
          <NavigatorInspector />
          <div>Không còn guard</div>
        </MemoryRouter>
      </StrictMode>,
    );

    expect(navigatorHolder.ref?.push).toBe(navigatorHolder.initialPush);
  });

  it('never blocks logout or session-expiry redirects and wipes scoped Admin drafts on clearAuth()', async () => {
    useAuthStore.getState().setAuth({
      user: {
        id: 77,
        email: 'admin77@medflow.vn',
        fullName: 'Quản trị 77',
        roles: ['HOSPITAL_ADMIN'],
        patientCode: null,
        staffCode: 'HOS01-ADM-77',
      },
      accessToken: 'tok-77',
      refreshToken: 'rtok-77',
    });

    // Scoped keys isolate user + hospital + formType and strip forbidden fields
    const keyA = buildAdminDraftStorageKey('department-create', {
      userId: 77,
      hospitalScope: 'HOS-01',
    });
    const keyB = buildAdminDraftStorageKey('department-create', {
      userId: 88,
      hospitalScope: 'HOS-02',
    });
    expect(keyA).not.toBe(keyB);

    const saved = saveAdminDraft(
      'department-create',
      { code: 'DEP-01', password: 'secret-should-be-stripped' },
      { userId: 77, hospitalScope: 'HOS-01' },
    );
    expect(saved.ok).toBe(true);
    expect(
      loadAdminDraft<Record<string, unknown>>('department-create', {
        userId: 77,
        hospitalScope: 'HOS-01',
      }),
    ).toEqual({ code: 'DEP-01' });
    expect(loadAdminDraft('department-create', { userId: 88, hospitalScope: 'HOS-02' })).toBeNull();

    function ProtectedDirtyScreen() {
      const navigate = useNavigate();
      const isAuthenticated = useAuthStore((s) => s.isAuthenticated);
      const guard = useUnsavedChanges(true);

      useEffect(() => {
        if (!isAuthenticated) {
          navigate('/login', { replace: true });
        }
      }, [isAuthenticated, navigate]);

      return (
        <div>
          <span>Màn hình Quản trị đang nhập dở</span>
          <UnsavedChangesModal
            open={guard.isBlocked}
            onStay={guard.cancelNavigation}
            onLeave={guard.confirmNavigation}
          />
        </div>
      );
    }

    render(
      <MemoryRouter initialEntries={['/admin/departments/new']}>
        <Routes>
          <Route path="/admin/departments/new" element={<ProtectedDirtyScreen />} />
          <Route path="/login" element={<div>Trang Đăng nhập</div>} />
        </Routes>
      </MemoryRouter>,
    );

    expect(screen.getByText('Màn hình Quản trị đang nhập dở')).toBeInTheDocument();

    // Simulate session expiration / logout
    act(() => {
      useAuthStore.getState().clearAuth();
    });

    expect(await screen.findByText('Trang Đăng nhập')).toBeInTheDocument();
    expect(screen.queryByTestId('unsaved-changes-modal')).not.toBeInTheDocument();

    const unloadAfterLogout = new Event('beforeunload', { cancelable: true });
    window.dispatchEvent(unloadAfterLogout);
    expect(unloadAfterLogout.defaultPrevented).toBe(false);

    // Scoped drafts were wiped on logout
    expect(loadAdminDraft('department-create', { userId: 77, hospitalScope: 'HOS-01' })).toBeNull();

    clearAllAdminDrafts();
  });
});
