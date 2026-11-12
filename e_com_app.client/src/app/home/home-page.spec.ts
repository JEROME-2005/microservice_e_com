import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HomePage } from './home-page';

describe('HomePage', () => {
  let component: HomePage;
  let fixture: ComponentFixture<HomePage>;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      declarations: [HomePage],
      imports: [HttpClientTestingModule]
    }).compileComponents();
  });

  beforeEach(() => {
    fixture = TestBed.createComponent(HomePage);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should create the home page', () => {
    expect(component).toBeTruthy();
  });

  it('should load categories through the gateway', () => {
    const mock = [{ id: '1', slug: 'fashion', name: 'Fashion' }];

    component.ngOnInit();

    const req = httpMock.expectOne('/api/v1/categories');
    expect(req.request.method).toEqual('GET');
    req.flush(mock);

    expect(component['categories']()).toEqual(mock);
  });
});
