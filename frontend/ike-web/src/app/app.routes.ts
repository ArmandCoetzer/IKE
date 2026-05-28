import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';
import { permissionGuard } from './core/guards/permission.guard';
import { MainLayoutComponent } from './layout/main-layout/main-layout.component';

export const routes: Routes = [
  { path: 'login', loadComponent: () => import('./features/auth/login/login.component').then(m => m.LoginComponent) },
  { path: 'forgot-password', loadComponent: () => import('./features/auth/forgot-password/forgot-password.component').then(m => m.ForgotPasswordComponent) },
  { path: 'reset-password', loadComponent: () => import('./features/auth/reset-password/reset-password.component').then(m => m.ResetPasswordComponent) },
  // Self-admin registration disabled intentionally; keep code for reference.
  // { path: 'register', loadComponent: () => import('./features/auth/register/register.component').then(m => m.RegisterComponent) },
  { path: 'accept-invite', loadComponent: () => import('./features/auth/accept-invite/accept-invite.component').then(m => m.AcceptInviteComponent) },
  {
    path: '',
    component: MainLayoutComponent,
    canActivate: [authGuard],
    children: [
      { path: 'dashboard', loadComponent: () => import('./features/dashboard/dashboard.component').then(m => m.DashboardComponent) },
      {
        path: 'start-new-job',
        loadComponent: () => import('./features/start-new-job/start-new-job.component').then(m => m.StartNewJobComponent),
        canActivate: [permissionGuard],
        data: { permissionsAny: ['CreateJobCards', 'ProcessRequests'], disallowRoles: ['Client'] }
      },
      {
        path: 'clients',
        loadComponent: () => import('./features/clients/clients-list/clients-list.component').then(m => m.ClientsListComponent),
        canActivate: [permissionGuard],
        data: { permissions: ['ViewClients'], disallowRoles: ['Client'] }
      },
      {
        path: 'clients/new',
        loadComponent: () => import('./features/clients/client-add/client-add.component').then(m => m.ClientAddComponent),
        canActivate: [permissionGuard],
        data: { permissions: ['ViewClients'], disallowRoles: ['Client'] }
      },
      {
        path: 'clients/:id',
        loadComponent: () => import('./features/clients/client-detail/client-detail.component').then(m => m.ClientDetailComponent),
        canActivate: [permissionGuard],
        data: { permissions: ['ViewClients'], disallowRoles: ['Client'] }
      },
      {
        path: 'clients/:id/edit',
        loadComponent: () => import('./features/clients/client-edit/client-edit.component').then(m => m.ClientEditComponent),
        canActivate: [permissionGuard],
        data: { permissions: ['ViewClients'], disallowRoles: ['Client'] }
      },
      {
        path: 'users',
        loadComponent: () => import('./features/users/users-list/users-list.component').then(m => m.UsersListComponent),
        canActivate: [permissionGuard],
        data: { permissions: ['ViewUsers'], disallowRoles: ['Client'] }
      },
      {
        path: 'users/new',
        loadComponent: () => import('./features/users/user-add/user-add.component').then(m => m.UserAddComponent),
        canActivate: [permissionGuard],
        data: { permissions: ['ViewUsers'], disallowRoles: ['Client'] }
      },
      {
        path: 'users/:id',
        loadComponent: () => import('./features/users/user-detail/user-detail.component').then(m => m.UserDetailComponent),
        canActivate: [permissionGuard],
        data: { permissions: ['ViewUsers'], disallowRoles: ['Client'] }
      },
      {
        path: 'service-requests',
        loadComponent: () => import('./features/service-requests/service-requests-list/service-requests-list.component').then(m => m.ServiceRequestsListComponent),
        canActivate: [permissionGuard],
        data: { permissions: ['ViewRequests'] }
      },
      {
        path: 'service-requests/new',
        loadComponent: () => import('./features/service-requests/service-request-add/service-request-add.component').then(m => m.ServiceRequestAddComponent),
        canActivate: [permissionGuard],
        data: { permissions: ['ViewRequests'], roles: ['Client'] }
      },
      {
        path: 'service-requests/:id/edit',
        loadComponent: () => import('./features/service-requests/service-request-edit/service-request-edit.component').then(m => m.ServiceRequestEditComponent),
        canActivate: [permissionGuard],
        data: { permissions: ['ViewRequests'] }
      },
      {
        path: 'service-requests/:id',
        loadComponent: () => import('./features/service-requests/service-request-detail/service-request-detail.component').then(m => m.ServiceRequestDetailComponent),
        canActivate: [permissionGuard],
        data: { permissions: ['ViewRequests'] }
      },
      {
        path: 'tracking',
        loadComponent: () => import('./features/tracking/tracking-map.component').then(m => m.TrackingMapComponent),
        canActivate: [permissionGuard],
        data: { permissions: ['ViewJobCards'], disallowRoles: ['Client'] }
      },
      {
        path: 'job-cards',
        loadComponent: () => import('./features/job-cards/job-cards-list/job-cards-list.component').then(m => m.JobCardsListComponent),
        canActivate: [permissionGuard],
        data: { permissions: ['ViewJobCards'] }
      },
      {
        path: 'job-cards/new',
        loadComponent: () => import('./features/job-cards/job-card-add/job-card-add.component').then(m => m.JobCardAddComponent),
        canActivate: [permissionGuard],
        data: { permissions: ['CreateJobCards'], disallowRoles: ['Client'] }
      },
      {
        path: 'job-cards/:id/edit',
        loadComponent: () => import('./features/job-cards/job-card-edit/job-card-edit.component').then(m => m.JobCardEditComponent),
        canActivate: [permissionGuard],
        data: { permissions: ['ViewJobCards'], disallowRoles: ['Client'] }
      },
      {
        path: 'job-cards/:id',
        loadComponent: () => import('./features/job-cards/job-card-detail/job-card-detail.component').then(m => m.JobCardDetailComponent),
        canActivate: [permissionGuard],
        data: { permissions: ['ViewJobCards'] }
      },
      {
        path: 'permits',
        loadComponent: () => import('./features/permits/permits-list/permits-list.component').then(m => m.PermitsListComponent),
        canActivate: [permissionGuard],
        data: { permissions: ['ViewJobCards'], disallowRoles: ['Client'] }
      },
      {
        path: 'permits/new',
        loadComponent: () => import('./features/permits/permit-type-add/permit-type-add.component').then(m => m.PermitTypeAddComponent),
        canActivate: [permissionGuard],
        data: { permissions: ['ViewJobCards'], disallowRoles: ['Client'] }
      },
      {
        path: 'permits/:id',
        loadComponent: () => import('./features/permits/permit-type-detail/permit-type-detail.component').then(m => m.PermitTypeDetailComponent),
        canActivate: [permissionGuard],
        data: { permissions: ['ViewJobCards'], disallowRoles: ['Client'] }
      },
      { path: 'training', loadComponent: () => import('./features/training/training.component').then(m => m.TrainingComponent) },
      { path: 'training/course/:id', loadComponent: () => import('./features/training/training-course/training-course.component').then(m => m.TrainingCourseComponent) },
      { path: 'training/module/:id', loadComponent: () => import('./features/training/training-module/training-module.component').then(m => m.TrainingModuleComponent) },
      { path: 'training/quiz/:id', loadComponent: () => import('./features/training/training-quiz/training-quiz.component').then(m => m.TrainingQuizComponent) },
      {
        path: 'training-setup',
        loadComponent: () => import('./features/training/training-setup.component').then(m => m.TrainingSetupComponent),
        canActivate: [permissionGuard],
        data: { permissions: ['ManageTraining'], disallowRoles: ['Client'] }
      },
      {
        path: 'invoices',
        loadComponent: () => import('./features/invoices/invoices-list/invoices-list.component').then(m => m.InvoicesListComponent),
        canActivate: [permissionGuard],
        data: { permissions: ['ViewReports'] }
      },
      {
        path: 'invoices/new',
        loadComponent: () => import('./features/invoices/invoice-add/invoice-add.component').then(m => m.InvoiceAddComponent),
        canActivate: [permissionGuard],
        data: { permissions: ['ManageInvoices'] }
      },
      {
        path: 'invoices/:id/edit',
        loadComponent: () => import('./features/invoices/invoice-edit/invoice-edit.component').then(m => m.InvoiceEditComponent),
        canActivate: [permissionGuard],
        data: { permissions: ['ManageInvoices'] }
      },
      {
        path: 'invoices/:id',
        loadComponent: () => import('./features/invoices/invoice-detail/invoice-detail.component').then(m => m.InvoiceDetailComponent),
        canActivate: [permissionGuard],
        data: { permissions: ['ViewReports'] }
      },
      {
        path: 'quotes',
        loadComponent: () => import('./features/quotes/quotes-list/quotes-list.component').then(m => m.QuotesListComponent),
        canActivate: [permissionGuard],
        data: { permissions: ['ViewPurchaseOrders'] }
      },
      {
        path: 'quotes/new',
        loadComponent: () => import('./features/quotes/quote-add/quote-add.component').then(m => m.QuoteAddComponent),
        canActivate: [permissionGuard],
        data: { permissions: ['ManagePurchaseOrders'] }
      },
      {
        path: 'quotes/:id/edit',
        loadComponent: () => import('./features/quotes/quote-edit/quote-edit.component').then(m => m.QuoteEditComponent),
        canActivate: [permissionGuard],
        data: { permissions: ['ManagePurchaseOrders'] }
      },
      {
        path: 'quotes/:id',
        loadComponent: () => import('./features/quotes/quote-detail/quote-detail.component').then(m => m.QuoteDetailComponent),
        canActivate: [permissionGuard],
        data: { permissions: ['ViewPurchaseOrders'] }
      },
      {
        path: 'purchase-orders',
        loadComponent: () => import('./features/purchase-orders/purchase-orders-list/purchase-orders-list.component').then(m => m.PurchaseOrdersListComponent),
        canActivate: [permissionGuard],
        data: { permissions: ['ViewPurchaseOrders'] }
      },
      {
        path: 'purchase-orders/new',
        loadComponent: () => import('./features/purchase-orders/purchase-order-add/purchase-order-add.component').then(m => m.PurchaseOrderAddComponent),
        canActivate: [permissionGuard],
        data: { permissions: ['ManagePurchaseOrders'] }
      },
      {
        path: 'purchase-orders/:id/edit',
        loadComponent: () => import('./features/purchase-orders/purchase-order-edit/purchase-order-edit.component').then(m => m.PurchaseOrderEditComponent),
        canActivate: [permissionGuard],
        data: { permissions: ['ManagePurchaseOrders'] }
      },
      {
        path: 'purchase-orders/:id',
        loadComponent: () => import('./features/purchase-orders/purchase-order-detail/purchase-order-detail.component').then(m => m.PurchaseOrderDetailComponent),
        canActivate: [permissionGuard],
        data: { permissions: ['ViewPurchaseOrders'] }
      },
      {
        path: 'parts',
        loadComponent: () => import('./features/parts/parts-list/parts-list.component').then(m => m.PartsListComponent),
        canActivate: [permissionGuard],
        data: { permissions: ['ViewPurchaseOrders'], disallowRoles: ['Client'] }
      },
      {
        path: 'parts/new',
        loadComponent: () => import('./features/parts/part-add/part-add.component').then(m => m.PartAddComponent),
        canActivate: [permissionGuard],
        data: { permissions: ['ViewPurchaseOrders'], disallowRoles: ['Client'] }
      },
      {
        path: 'parts/:id',
        loadComponent: () => import('./features/parts/part-detail/part-detail.component').then(m => m.PartDetailComponent),
        canActivate: [permissionGuard],
        data: { permissions: ['ViewPurchaseOrders'], disallowRoles: ['Client'] }
      },
      {
        path: 'parts/:id/edit',
        loadComponent: () => import('./features/parts/part-edit/part-edit.component').then(m => m.PartEditComponent),
        canActivate: [permissionGuard],
        data: { permissions: ['ViewPurchaseOrders'], disallowRoles: ['Client'] }
      },
      {
        path: 'suppliers',
        loadComponent: () => import('./features/suppliers/suppliers-list/suppliers-list.component').then(m => m.SuppliersListComponent),
        canActivate: [permissionGuard],
        data: { permissions: ['ViewPurchaseOrders'], disallowRoles: ['Client'] }
      },
      {
        path: 'suppliers/new',
        loadComponent: () => import('./features/suppliers/supplier-add/supplier-add.component').then(m => m.SupplierAddComponent),
        canActivate: [permissionGuard],
        data: { permissions: ['ManagePurchaseOrders'], disallowRoles: ['Client'] }
      },
      {
        path: 'suppliers/:id/edit',
        loadComponent: () => import('./features/suppliers/supplier-edit/supplier-edit.component').then(m => m.SupplierEditComponent),
        canActivate: [permissionGuard],
        data: { permissions: ['ManagePurchaseOrders'], disallowRoles: ['Client'] }
      },
      {
        path: 'supplier-quote-requests',
        loadComponent: () => import('./features/supplier-quote-requests/supplier-quote-request-add/supplier-quote-request-add.component').then(m => m.SupplierQuoteRequestAddComponent),
        canActivate: [permissionGuard],
        data: { permissions: ['ViewPurchaseOrders'], disallowRoles: ['Client'] }
      },
      {
        path: 'supplier-quote-requests/new',
        redirectTo: 'supplier-quote-requests',
        pathMatch: 'full'
      },
      {
        path: 'supplier-quote-requests/list',
        loadComponent: () => import('./features/supplier-quote-requests/supplier-quote-requests-list/supplier-quote-requests-list.component').then(m => m.SupplierQuoteRequestsListComponent),
        canActivate: [permissionGuard],
        data: { permissions: ['ViewPurchaseOrders'], disallowRoles: ['Client'] }
      },
      {
        path: 'reports',
        loadComponent: () => import('./features/reports/reports.component').then(m => m.ReportsComponent),
        canActivate: [permissionGuard],
        data: { permissions: ['ViewReports'] }
      },
      { path: 'notifications', loadComponent: () => import('./features/notifications/notifications.component').then(m => m.NotificationsComponent), canActivate: [permissionGuard], data: { permissionsAny: ['ViewRequests', 'ViewJobCards', 'ViewReports', 'ViewPurchaseOrders', 'ViewUsers'] } },
      {
        path: 'dev/audit/errors',
        loadComponent: () => import('./features/dev/audit-errors/audit-errors.component').then(m => m.AuditErrorsComponent),
        canActivate: [permissionGuard],
        data: { disallowRoles: ['Client'] }
      },
      {
        path: 'dev/bugs',
        loadComponent: () => import('./features/dev/bugs/bugs.component').then(m => m.BugsComponent),
        canActivate: [permissionGuard],
        data: { disallowRoles: ['Client'] }
      },
      { path: 'my-account', loadComponent: () => import('./features/auth/my-account/my-account.component').then(m => m.MyAccountComponent), canActivate: [permissionGuard], data: { permissionsAny: ['ViewRequests', 'ViewJobCards', 'ViewReports', 'ViewPurchaseOrders', 'ViewUsers'] } },
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' }
    ]
  },
  { path: '**', redirectTo: 'dashboard' }
];
